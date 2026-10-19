using System.Globalization;
using System.Text;
namespace PCDoctor;

// Dependency-free PDF writer (A4, Helvetica/Courier, plain text with colour). Non-Latin characters become '?'.
public static class PdfReport {
    // Styles: 0 normal, 1 heading, 2 title, 10 ok, 11 warn, 12 problem, 20 log (monospace)
    public static List<(string T, int S)> Build(IEnumerable<Finding> findings, IEnumerable<DriverItem> drivers, string log, string score, bool redact) {
        var l = new List<(string, int)>();
        void Add(string t, int s = 0, int w = 95) { foreach (var x in Wrap(t, s == 20 ? 105 : w)) l.Add((x, s)); }
        var f = findings.ToList(); var d = drivers.ToList();
        Add("PC Doctor - System Report", 2);
        Add($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        Add($"Windows: {Environment.OSVersion.VersionString}  ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        Add($"Processors: {Environment.ProcessorCount}");
        if (!redact) Add($"Computer: {Environment.MachineName}   User: {Environment.UserName}");
        Add("", 0); Add("Summary", 1);
        Add($"Health score: {score}/100   Problems: {f.Count(x => x.Level == Level.Problem)}   Warnings: {f.Count(x => x.Level == Level.Warn)}   Checks passed: {f.Count(x => x.Level == Level.Ok)}");
        Add("", 0); Add("Diagnostic findings", 1);
        if (f.Count == 0) Add("No scan results yet. Run a scan first.");
        foreach (var x in f.OrderByDescending(x => x.Level)) {
            Add($"[{(x.Level == Level.Ok ? "OK" : x.Level == Level.Warn ? "WARNING" : "PROBLEM")}] {x.Area}: {x.Check} - {x.Result}", 10 + (int)x.Level, 92);
            if (x.Advice.Length > 0 && x.Level != Level.Ok) Add("    Advice: " + x.Advice);
        }
        Add("", 0); Add("Driver updates found", 1);
        if (d.Count == 0) Add("None listed (run 'Search for drivers' to check Windows Update).");
        foreach (var x in d) Add($"- {x.Title}  ({x.Maker}, {x.Class}, {x.Mb} MB)");
        Add("", 0); Add("Activity log", 1);
        if (string.IsNullOrWhiteSpace(log)) Add("No actions recorded.");
        else foreach (var line in log.Replace("\r", "").Split('\n')) Add(line, 20);
        if (redact) for (int i = 0; i < l.Count; i++) foreach (var n in new[] { Environment.MachineName, Environment.UserName })
            if (n.Length > 0) l[i] = (l[i].Item1.Replace(n, "<redacted>"), l[i].Item2);
        return l;
    }

    static IEnumerable<string> Wrap(string t, int max) {
        foreach (var raw in t.Replace("\r", "").Split('\n')) {
            var s = raw;
            if (s.Length == 0) { yield return ""; continue; }
            while (s.Length > max) { int cut = s.LastIndexOf(' ', max); if (cut <= 0) cut = max; yield return s[..cut]; s = s[cut..].TrimStart(); }
            yield return s;
        }
    }

    static string Esc(string t) {
        var sb = new StringBuilder();
        foreach (var c in t) { if (c < 32 || c > 126) sb.Append('?'); else { if (c is '(' or ')' or '\\') sb.Append('\\'); sb.Append(c); } }
        return sb.ToString();
    }

    static string Op(string t, int s, double y) {
        var (font, size, col) = s switch {
            2 => ("F2", 20, "0.1 0.1 0.1"), 1 => ("F2", 13, "0.18 0.4 0.9"), 10 => ("F2", 10, "0.12 0.6 0.25"),
            11 => ("F2", 10, "0.8 0.55 0"), 12 => ("F2", 10, "0.85 0.2 0.15"), 20 => ("F3", 8.5, "0.3 0.3 0.3"), _ => ("F1", 10, "0.15 0.15 0.15") };
        return string.Create(CultureInfo.InvariantCulture, $"BT /{font} {size} Tf {col} rg 50 {y:F1} Td ({Esc(t)}) Tj ET\n");
    }

    public static void Save(string path, List<(string T, int S)> lines) {
        var pages = new List<string>(); var sb = new StringBuilder(); double y = 792;
        foreach (var (t, s) in lines) {
            double h = s switch { 2 => 28, 1 => 22, 20 => 11.5, _ => 14 };
            if (y - h < 60) { pages.Add(sb.ToString()); sb.Clear(); y = 792; }
            y -= h; sb.Append(Op(t, s, y));
        }
        pages.Add(sb.ToString());

        var ms = new MemoryStream(); var offs = new List<long>();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        void Obj(string body) { offs.Add(ms.Length); W($"{offs.Count} 0 obj\n{body}\nendobj\n"); }
        W("%PDF-1.4\n");
        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        Obj("<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{6 + 2 * i} 0 R")) + $"] /Count {pages.Count} >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < pages.Count; i++) {
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {7 + 2 * i} 0 R >>");
            var c = pages[i] + $"BT /F1 8 Tf 0.5 0.5 0.5 rg 50 30 Td (PC Doctor report  -  Page {i + 1} of {pages.Count}) Tj ET\n";
            Obj($"<< /Length {c.Length} >>\nstream\n{c}endstream");
        }
        long xref = ms.Length;
        W($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        File.WriteAllBytes(path, ms.ToArray());
    }
}
