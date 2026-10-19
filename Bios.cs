using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
namespace PCDoctor;

public class BiosItem {
    public string Name { get; init; } = ""; public string Value { get; init; } = ""; public string Options { get; init; } = "";
    public bool Disabled => Regex.IsMatch(Value, "disable", RegexOptions.IgnoreCase);
    public string? EnableOption => Options.Split(',').Select(x => x.Trim()).FirstOrDefault(x => Regex.IsMatch(x, "^enable", RegexOptions.IgnoreCase));
    public Visibility CanEnable => Disabled && EnableOption != null ? Visibility.Visible : Visibility.Collapsed;
    public string Info => Options.Length > 0 ? "Options: " + Options : "";
    public Brush Dot => new SolidColorBrush(Disabled ? Color.FromRgb(255, 69, 58) : Color.FromRgb(52, 199, 89));
}

public static class Bios {
    public static string Vendor() {
        using var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_ComputerSystem");
        var m = $"{s.Get().Cast<ManagementObject>().First()["Manufacturer"]}";
        return Regex.IsMatch(m, "lenovo", RegexOptions.IgnoreCase) ? "Lenovo" : Regex.IsMatch(m, "^hp|hewlett", RegexOptions.IgnoreCase) ? "HP" : Regex.IsMatch(m, "dell", RegexOptions.IgnoreCase) ? "Dell" : m;
    }

    public static (List<BiosItem> Relevant, int Total) Read() {
        var all = new List<BiosItem>(); var v = Vendor();
        if (v == "Lenovo") {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentSetting FROM Lenovo_BiosSetting");
            foreach (ManagementObject o in s.Get()) {
                var cs = $"{o["CurrentSetting"]}"; if (!cs.Contains(',')) continue;
                var name = cs.Split(',')[0]; var rest = cs[(name.Length + 1)..]; var opt = Regex.Match(rest, @"\[Optional:(.*)\]");
                all.Add(new BiosItem { Name = name, Value = rest.Split(';')[0], Options = opt.Success ? opt.Groups[1].Value : "" });
            }
        } else if (v == "HP") {
            using var s = new ManagementObjectSearcher(@"root\HP\InstrumentedBIOS", "SELECT Name,Value,PossibleValues FROM HP_BIOSEnumeration");
            foreach (ManagementObject o in s.Get())
                all.Add(new BiosItem { Name = $"{o["Name"]}", Value = $"{o["Value"]}", Options = string.Join(",", (string[]?)o["PossibleValues"] ?? Array.Empty<string>()) });
        } else if (v == "Dell") {
            using var s = new ManagementObjectSearcher(@"root\dcim\sysman", "SELECT AttributeName,CurrentValue,PossibleValue FROM DCIM_BIOSEnumeration");
            foreach (ManagementObject o in s.Get())
                all.Add(new BiosItem { Name = $"{o["AttributeName"]}", Value = string.Join(",", (string[]?)o["CurrentValue"] ?? Array.Empty<string>()), Options = string.Join(",", (string[]?)o["PossibleValue"] ?? Array.Empty<string>()) });
        } else throw new InvalidOperationException($"BIOS settings are not exposed to Windows on '{v}' laptops.");
        return (all.Where(i => Regex.IsMatch(i.Name, "touch|track|point|i2c|hid|pad|fn|hotkey", RegexOptions.IgnoreCase)).ToList(), all.Count);
    }

    static object? Call(string ns, string cls, string method, Dictionary<string, object> args) {
        using var mc = new ManagementClass($@"\\.\{ns}:{cls}");
        foreach (ManagementObject o in mc.GetInstances()) {
            var inp = o.GetMethodParameters(method); foreach (var kv in args) inp[kv.Key] = kv.Value;
            return o.InvokeMethod(method, inp, null)?["return"];
        }
        throw new InvalidOperationException("BIOS interface not available.");
    }

    // Returns null on success, otherwise an error message.
    public static string? Enable(BiosItem it, string pwd) {
        try {
            switch (Vendor()) {
                case "Lenovo": {
                    var arg = pwd.Length > 0 ? $"{it.Name},{it.EnableOption},{pwd},ascii,us" : $"{it.Name},{it.EnableOption}";
                    var r = Call(@"root\wmi", "Lenovo_SetBiosSetting", "SetBiosSetting", new() { ["parameter"] = arg })?.ToString();
                    if (r != "Success") return "BIOS said: " + r;
                    var r2 = Call(@"root\wmi", "Lenovo_SaveBiosSettings", "SaveBiosSettings", new() { ["parameter"] = pwd.Length > 0 ? $"{pwd},ascii,us" : "" })?.ToString();
                    return r2 == "Success" ? null : "Save failed: " + r2;
                }
                case "HP": {
                    var r = Call(@"root\HP\InstrumentedBIOS", "HP_BIOSSettingInterface", "SetBIOSSetting", new() { ["Name"] = it.Name, ["Value"] = it.EnableOption!, ["Password"] = "<utf-16/>" + pwd });
                    return Convert.ToInt32(r) == 0 ? null : "BIOS returned code " + r;
                }
                default: return "On this PC, changes need the maker's own tool (for Dell: Dell Command | Configure) or the BIOS setup screen (F2 at startup).";
            }
        } catch (Exception ex) { return ex.Message; }
    }

    static readonly Dictionary<string, string> Tools = new() { ["Lenovo"] = "Lenovo.SystemUpdate", ["HP"] = "HP.HPSupportAssistant", ["Dell"] = "Dell.CommandUpdate" };
    public static string? ToolId() => Tools.TryGetValue(Vendor(), out var id) ? id : null;
    public static bool InstallTool(string id) {
        try { Process.Start(new ProcessStartInfo("winget", $"install --id {id} -e --accept-package-agreements --accept-source-agreements") { UseShellExecute = true }); return true; }
        catch { return false; }
    }
    public static void OpenSupportPage() {
        using var s = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
        var m = s.Get().Cast<ManagementObject>().First();
        Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + Uri.EscapeDataString($"{m["Manufacturer"]} {m["Model"]} drivers support")) { UseShellExecute = true });
    }
}
