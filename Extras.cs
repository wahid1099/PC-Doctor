using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
namespace PCDoctor;

public class AppSettings {
    public bool Dark { get; set; } = true;
    public string UpdateUrl { get; set; } = "";   // https URL of your update.json (see update.sample.json)
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCDoctor");
    public static string FilePath => Path.Combine(Dir, "settings.json");
    public static AppSettings Load() {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        var v = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme");
        return new AppSettings { Dark = !(v is int i && i == 1) };
    }
    public void Save() { try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
}

// Data-driven fixes: fixes.json next to the exe and/or in %AppData%\PCDoctor. Same id replaces a built-in fix.
public class JsonFix {
    public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Desc { get; set; } = ""; public string Keys { get; set; } = "";
    public List<Dictionary<string, string>> Steps { get; set; } = new();
}
public static partial class Catalog {
    public static void LoadExtra(Action<string> log) {
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "fixes.json"), Path.Combine(AppSettings.Dir, "fixes.json") }) {
            if (!File.Exists(path)) continue;
            try {
                var list = JsonSerializer.Deserialize<List<JsonFix>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                foreach (var f in list.Where(f => f.Id.Length > 0 && f.Title.Length > 0)) {
                    Fixers.RemoveAll(x => x.Id == f.Id);
                    var steps = f.Steps;
                    Fixers.Add(new Fixer(f.Id, f.Title, f.Desc, f.Keys.Length > 0 ? f.Keys : f.Id, async l => {
                        foreach (var st in steps) {
                            string X(string k) => Environment.ExpandEnvironmentVariables(st[k]);
                            if (st.ContainsKey("cmd")) await Sys.Cmd(X("cmd"), l);
                            else if (st.ContainsKey("delete")) Wipe(X("delete"));
                            else if (st.ContainsKey("open")) Process.Start(new ProcessStartInfo(X("open")) { UseShellExecute = true });
                            else if (st.ContainsKey("log")) l(st["log"]);
                        }
                    }));
                }
            } catch (Exception ex) { log($"Could not load {path}: {ex.Message}"); }
        }
    }
}

public class UpdateFeed {
    public string Version { get; set; } = ""; public string Url { get; set; } = ""; public string Sha256 { get; set; } = ""; public string Notes { get; set; } = "";
    public string FixesUrl { get; set; } = ""; public string FixesSha256 { get; set; } = "";
}
public static class Updater {
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0);

    public static async Task<UpdateFeed> Fetch(string url) {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Update URL must use https.");
        return JsonSerializer.Deserialize<UpdateFeed>(await Http.GetStringAsync(url), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Empty update feed.");
    }
    public static bool IsNewer(UpdateFeed f) => Version.TryParse(f.Version, out var v) && v > Current;

    // Downloads over https and refuses anything whose SHA-256 does not match the feed.
    public static async Task<byte[]> Download(string url, string sha) {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || sha.Length != 64) throw new InvalidOperationException("Update needs an https URL and a SHA-256 hash.");
        var b = await Http.GetByteArrayAsync(url);
        if (!Convert.ToHexString(SHA256.HashData(b)).Equals(sha, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Download failed the integrity check; update cancelled.");
        return b;
    }
    public static async Task<bool> UpdateFixes(UpdateFeed f) {
        if (f.FixesUrl.Length == 0) return false;
        var b = await Download(f.FixesUrl, f.FixesSha256);
        JsonSerializer.Deserialize<List<JsonFix>>(b, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); // must parse
        Directory.CreateDirectory(AppSettings.Dir); await File.WriteAllBytesAsync(Path.Combine(AppSettings.Dir, "fixes.json"), b); return true;
    }
    public static void ApplyAndRestart(byte[] exe) {
        var cur = Environment.ProcessPath!; var tmp = Path.Combine(Path.GetTempPath(), "PCDoctor-new.exe"); var cmd = Path.Combine(Path.GetTempPath(), "pcdoctor-update.cmd");
        File.WriteAllBytes(tmp, exe);
        File.WriteAllText(cmd, $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\ncopy /y \"{tmp}\" \"{cur}\" >nul\r\nstart \"\" \"{cur}\"\r\ndel \"{tmp}\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
