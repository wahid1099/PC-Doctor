using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;
namespace PCDoctor;

public enum Level { Ok, Warn, Problem }

public class Finding {
    public string Area { get; init; } = ""; public string Check { get; init; } = "";
    public string Result { get; init; } = ""; public string Advice { get; init; } = "";
    public Level Level { get; init; }
    public Brush Dot => new SolidColorBrush(Level switch { Level.Ok => Color.FromRgb(52, 199, 89), Level.Warn => Color.FromRgb(255, 184, 0), _ => Color.FromRgb(255, 69, 58) });
}
public class DriverItem {
    public string Id { get; init; } = ""; public string Title { get; init; } = ""; public string Maker { get; init; } = "";
    public string Class { get; init; } = ""; public double Mb { get; init; }
    public string Info => $"{Maker}  |  {Class}  |  {Mb} MB";
    public bool Selected { get; set; } = true;
}
public record Fixer(string Id, string Title, string Desc, string Keys, Func<Action<string>, Task> Run);
public class FixItem {
    public Fixer Fixer { get; init; } = null!; public bool Selected { get; set; }
    public string Title => Fixer.Title; public string Desc => Fixer.Desc;
}

public static class Sys {
    public static async Task<int> Cmd(string line, Action<string>? log = null) {
        log?.Invoke("> " + line);
        int i = line.IndexOf(' '); string file = i < 0 ? line : line[..i], args = i < 0 ? "" : line[(i + 1)..];
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        try {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(); await Task.WhenAll(o, e);
            if (p.ExitCode != 0) log?.Invoke($"  exit code {p.ExitCode}");
            return p.ExitCode;
        } catch (Exception ex) { log?.Invoke("  failed: " + ex.Message); return -1; }
    }
    public static async Task<string> Out(string file, params string[] args) {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(); await Task.WhenAll(o, e); return o.Result;
        } catch { return ""; }
    }
}

public static class Diagnostics {
    static string Advice(int c) => c switch {
        22 => "Device is disabled. Enable it in Device Manager.", 28 => "No driver installed. Use the Drivers page.",
        10 or 31 or 1 => "Driver problem. Update it on the Drivers page.", 43 => "Windows stopped this device. Update the driver or restart.",
        45 => "Device is not connected right now.", _ => "Try the Drivers page, then restart." };

    public static List<Finding> Scan() {
        var r = new List<Finding>();
        void Add(string a, string c, string res, Level l, string adv = "") => r.Add(new Finding { Area = a, Check = c, Result = res, Level = l, Advice = adv });
        try {
            using var s = new ManagementObjectSearcher("SELECT Name,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode<>0");
            var bad = s.Get().Cast<ManagementObject>().ToList();
            if (bad.Count == 0) Add("Devices", "Devices with errors", "None", Level.Ok);
            foreach (var d in bad) { int c = Convert.ToInt32(d["ConfigManagerErrorCode"]); Add("Devices", $"{d["Name"]}", $"Code {c}", c == 45 ? Level.Warn : Level.Problem, Advice(c)); }
        } catch (Exception ex) { Add("Devices", "Device scan", ex.Message, Level.Warn); }
        try {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_PointingDevice");
            var tp = s.Get().Cast<ManagementObject>().Select(m => $"{m["Name"]}").Where(n => Regex.IsMatch(n, "touch|track|synaptics|elan|precision", RegexOptions.IgnoreCase)).ToList();
            if (tp.Count > 0) Add("Input", "Touchpad", tp[0], Level.Ok);
            else Add("Input", "Touchpad", "Not detected", Level.Warn, "Check Fn key and BIOS setting, then install drivers.");
        } catch { }
        try {
            var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            int pct = (int)(100.0 * d.AvailableFreeSpace / d.TotalSize);
            Add("Storage", "System drive free space", $"{pct}% ({d.AvailableFreeSpace / 1073741824} GB)", pct < 10 ? Level.Problem : pct < 20 ? Level.Warn : Level.Ok, "Run 'Clean junk files' in Fix Center if low.");
        } catch { }
        try {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage"); scope.Connect();
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName,HealthStatus FROM MSFT_PhysicalDisk"));
            foreach (ManagementObject m in s.Get()) { bool ok = Convert.ToInt32(m["HealthStatus"]) == 0; Add("Storage", $"Disk: {m["FriendlyName"]}", ok ? "Healthy" : "Unhealthy", ok ? Level.Ok : Level.Problem, "Back up your data now if not healthy."); }
        } catch { }
        try {
            using var s = new ManagementObjectSearcher("SELECT FreePhysicalMemory,TotalVisibleMemorySize FROM Win32_OperatingSystem");
            var o = s.Get().Cast<ManagementObject>().First();
            int free = (int)(100.0 * Convert.ToDouble(o["FreePhysicalMemory"]) / Convert.ToDouble(o["TotalVisibleMemorySize"]));
            Add("Memory", "Free RAM", $"{free}%", free < 10 ? Level.Warn : Level.Ok, "Close heavy apps or restart.");
        } catch { }
        int days = (int)(Environment.TickCount64 / 86400000);
        Add("System", "Days since restart", days.ToString(), days > 14 ? Level.Warn : Level.Ok, "Restart to apply pending changes.");
        bool pend = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") != null
                 || Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") != null;
        Add("System", "Pending restart", pend ? "Yes" : "No", pend ? Level.Warn : Level.Ok, "Restart before installing drivers.");
        var wu = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv")?.GetValue("Start");
        bool wuOff = wu is int v && v == 4;
        Add("Updates", "Windows Update service", wuOff ? "Disabled" : "Available", wuOff ? Level.Problem : Level.Ok, "Driver installs need this service.");
        return r;
    }
}

public static class DriverService {
    const string Crit = "IsInstalled=0 and Type='Driver' and IsHidden=0";
    static dynamic New(string progId) => Activator.CreateInstance(Type.GetTypeFromProgID(progId)!)!;

    public static Task<List<DriverItem>> Search() => Task.Run(() => {
        dynamic s = New("Microsoft.Update.Session"); dynamic res = s.CreateUpdateSearcher().Search(Crit);
        var l = new List<DriverItem>();
        foreach (dynamic u in res.Updates)
            l.Add(new DriverItem { Id = (string)u.Identity.UpdateID, Title = (string)u.Title, Maker = (string)u.DriverManufacturer, Class = (string)u.DriverClass, Mb = Math.Round(Convert.ToDouble((object)u.MaxDownloadSize) / 1048576, 1) });
        return l;
    });

    public static string RestorePoint() {
        try {
            var mc = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var p = mc.GetMethodParameters("CreateRestorePoint");
            p["Description"] = "PC Doctor"; p["RestorePointType"] = 12; p["EventType"] = 100;
            mc.InvokeMethod("CreateRestorePoint", p, null); return "Restore point created.";
        } catch (Exception ex) { return "Restore point skipped: " + ex.Message; }
    }

    public static (List<string> Log, bool Reboot) Install(List<string> ids) {
        var log = new List<string> { RestorePoint() };
        dynamic s = New("Microsoft.Update.Session"); dynamic res = s.CreateUpdateSearcher().Search(Crit);
        dynamic coll = New("Microsoft.Update.UpdateColl");
        foreach (dynamic u in res.Updates) if (ids.Contains((string)u.Identity.UpdateID)) { if (!(bool)u.EULAAccepted) u.AcceptEula(); coll.Add(u); }
        if ((int)coll.Count == 0) { log.Add("Nothing to install."); return (log, false); }
        log.Add($"Downloading {(int)coll.Count} driver(s)...");
        dynamic d = s.CreateUpdateDownloader(); d.Updates = coll; d.Download();
        log.Add("Installing..."); dynamic inst = s.CreateUpdateInstaller(); inst.Updates = coll; dynamic r = inst.Install();
        for (int i = 0; i < (int)coll.Count; i++) {
            int code = (int)r.GetUpdateResult(i).ResultCode;
            log.Add($"{(string)coll.Item(i).Title}: {(code == 2 ? "Installed" : code == 3 ? "Installed with errors" : code == 4 ? "FAILED" : "Result " + code)}");
        }
        return (log, (bool)r.RebootRequired);
    }
}

public static partial class Catalog {
    static void Wipe(string dir) {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) try { File.Delete(f); } catch { }
        foreach (var d in Directory.EnumerateDirectories(dir)) try { Directory.Delete(d, true); } catch { }
    }
    static async Task Run(Action<string> log, params string[] cmds) { foreach (var c in cmds) await Sys.Cmd(c, log); }
    static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static List<Fixer> Fixers = new() {
        new("net", "Fix internet / Wi-Fi", "Flushes DNS and resets the network stack (restart may be needed).", "wifi|wi-fi|internet|network|connect|ethernet|dns|drop|online",
            l => Run(l, "ipconfig /flushdns", "netsh winsock reset", "netsh int ip reset", "ipconfig /renew")),
        new("audio", "Fix sound / microphone", "Restarts the Windows audio services.", "sound|audio|mic|speaker|headphone|volume|zoom|call|teams",
            l => Run(l, "net stop audiosrv /y", "net stop AudioEndpointBuilder /y", "net start AudioEndpointBuilder", "net start audiosrv")),
        new("periph", "Fix Bluetooth / printer / USB", "Clears stuck print jobs, restarts print and Bluetooth services, rescans hardware.", "bluetooth|printer|print|usb|scan|mouse|keyboard|device",
            async l => { await Run(l, "net stop spooler /y"); Wipe(Path.Combine(Win, "System32", "spool", "PRINTERS")); await Run(l, "net start spooler", "net stop bthserv /y", "net start bthserv", "pnputil /scan-devices"); }),
        new("clean", "Clean junk files", "Deletes temp files and reports the space freed.", "space|storage|full|junk|disk|slow|clean",
            l => { var d = new DriveInfo(Path.GetPathRoot(Win)!); long b = d.AvailableFreeSpace; Wipe(Path.GetTempPath()); Wipe(Path.Combine(Win, "Temp")); l($"Freed about {(d.AvailableFreeSpace - b) / 1048576} MB."); return Task.CompletedTask; }),
        new("update", "Reset Windows Update", "Resets update components when updates are stuck.", "update|stuck|failed|0x8|install",
            async l => { await Run(l, "net stop wuauserv /y", "net stop bits /y", "net stop cryptsvc /y");
                foreach (var p in new[] { Path.Combine(Win, "SoftwareDistribution"), Path.Combine(Win, "System32", "catroot2") }) try { if (Directory.Exists(p + ".old")) Directory.Delete(p + ".old", true); Directory.Move(p, p + ".old"); } catch (Exception ex) { l("  " + ex.Message); }
                await Run(l, "net start cryptsvc", "net start bits", "net start wuauserv"); }),
        new("repair", "Repair Windows files (DISM + SFC)", "Repairs corrupted system files. Can take 10-30 minutes.", "corrupt|crash|error|broken|repair|weird|freez|glitch",
            l => Run(l, "DISM.exe /Online /Cleanup-Image /RestoreHealth", "sfc.exe /scannow")),
        new("battery", "Battery and power report", "Saves a battery health report to your Desktop and opens it.", "battery|charge|power|drain|hot|heat|fan",
            async l => { var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "battery-report.html");
                await Run(l, $"powercfg /batteryreport /output \"{f}\""); if (File.Exists(f)) Process.Start(new ProcessStartInfo(f) { UseShellExecute = true }); }),
        new("crash", "Analyze crashes / blue screens", "Reads the last 7 days of error events and names the most frequent sources.", "blue|bsod|crash|restart|reboot|shutdown|freez|random",
            async l => {
                var o = await Sys.Out("wevtutil", "qe", "System", "/q:*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]", "/c:300", "/rd:true", "/f:text");
                var src = Regex.Matches(o, @"^\s*Source:\s*(.+?)\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
                l($"Error events in the last 7 days (up to 300 read): {src.Count}. Top sources:");
                foreach (var g in src.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(5)) l($"  {g.Count()} x {g.Key}");
                var bc = await Sys.Out("wevtutil", "qe", "System", "/q:*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)]]", "/c:5", "/rd:true", "/f:text");
                l($"Recent blue screens recorded: {Regex.Matches(bc, @"^Event\[", RegexOptions.Multiline).Count}");
                l("A source that repeats often usually points to the faulty driver or device. Update it on the Drivers page.");
            }),
        new("security", "Security checkup", "Checks Defender, firewall and disk encryption (report only).", "virus|security|malware|safe|hack|firewall|popup",
            async l => {
                try {
                    using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureAge FROM MSFT_MpComputerStatus");
                    var m = s.Get().Cast<ManagementObject>().First();
                    l($"Defender on: {m["AntivirusEnabled"]}; real-time: {m["RealTimeProtectionEnabled"]}; signatures {m["AntivirusSignatureAge"]} day(s) old");
                } catch { l("Defender status unavailable (another antivirus may be active)."); }
                foreach (var p in new[] { "DomainProfile", "StandardProfile", "PublicProfile" }) {
                    var v = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{p}")?.GetValue("EnableFirewall");
                    l($"Firewall {p}: {(v is int i && i == 1 ? "On" : "OFF or unknown")}");
                }
                var bl = Regex.Match(await Sys.Out("manage-bde", "-status", "C:"), @"Protection Status:\s*(.+)");
                l("Disk encryption (C:): " + (bl.Success ? bl.Groups[1].Value.Trim() : "not available on this edition"));
            }),
        new("startup", "Slow PC: show startup apps", "Lists programs that start at sign-in and opens Task Manager so you can disable heavy ones.", "slow|lag|freez|boot|startup|performance|hang",
            l => {
                using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_StartupCommand");
                var items = s.Get().Cast<ManagementObject>().ToList();
                l($"{items.Count} startup item(s):"); foreach (var m in items) l($"  {m["Name"]}");
                try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); l("Task Manager opened: go to Startup apps and disable what you do not need."); } catch { }
                return Task.CompletedTask;
            }),
    };
}
