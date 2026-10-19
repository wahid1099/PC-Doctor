using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace PCDoctor;

public partial class MainWindow : Window {
    readonly ObservableCollection<Finding> findings = new();
    readonly ObservableCollection<DriverItem> drivers = new();
    readonly ObservableCollection<FixItem> fixes = new();
    readonly ObservableCollection<BiosItem> bios = new();
    readonly AppSettings settings = AppSettings.Load();
    System.Windows.Forms.NotifyIcon? tray; bool exiting;
    bool busy;

    public MainWindow() {
        SetTheme(settings.Dark); settings.Save();
        InitializeComponent();
        VersionText.Text = "Version " + Updater.Current.ToString(3);
        FindingsList.ItemsSource = findings; DriversList.ItemsSource = drivers; FixList.ItemsSource = fixes; BiosList.ItemsSource = bios;
        Catalog.LoadExtra(Log);
        foreach (var f in Catalog.Fixers) fixes.Add(new FixItem { Fixer = f });
        InitTray();
        Loaded += async (_, _) => { await Scan(); if (settings.UpdateUrl.StartsWith("https://")) await CheckUpdates(true); };
    }

    void Nav_Checked(object s, RoutedEventArgs e) {
        if (PageLog == null) return;
        var t = (string)((RadioButton)s).Tag;
        var map = new Dictionary<string, UIElement> { ["Dash"] = PageDash, ["Drv"] = PageDrv, ["Fix"] = PageFix, ["Bios"] = PageBios, ["Log"] = PageLog };
        foreach (var kv in map) kv.Value.Visibility = kv.Key == t ? Visibility.Visible : Visibility.Collapsed;
        map[t].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    void Log(string m) { LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}\n"); LogBox.ScrollToEnd(); }

    async Task Guard(string text, Func<Task> work) {
        if (busy) return;
        busy = true; Pages.IsEnabled = false; BusyBar.Visibility = Visibility.Visible; StatusText.Text = text;
        try { await work(); }
        catch (Exception ex) { Log("Error: " + ex.Message); StatusText.Text = "Something failed: " + ex.Message; }
        finally { busy = false; Pages.IsEnabled = true; BusyBar.Visibility = Visibility.Collapsed; }
    }

    Task Scan() => Guard("Scanning...", async () => {
        var r = await Task.Run(Diagnostics.Scan);
        findings.Clear(); foreach (var f in r) { findings.Add(f); Log($"{f.Level}: {f.Area} - {f.Check} - {f.Result}"); }
        int p = r.Count(x => x.Level == Level.Problem), w = r.Count(x => x.Level == Level.Warn);
        int score = Math.Max(0, 100 - 15 * p - 5 * w);
        ScoreText.Text = score.ToString(); ScoreBar.Value = score;
        var col = score >= 80 ? Color.FromRgb(52, 199, 89) : score >= 50 ? Color.FromRgb(255, 184, 0) : Color.FromRgb(255, 69, 58);
        ScoreBar.Foreground = new SolidColorBrush(col); ScoreText.Foreground = new SolidColorBrush(col);
        ScoreLabel.Text = $"Health score: {p} problem(s), {w} warning(s)"; StatusText.Text = "Scan complete.";
    });

    Task DoSearch() => Guard("Searching Windows Update for drivers (can take a minute)...", async () => {
        var l = await DriverService.Search();
        drivers.Clear(); foreach (var d in l) drivers.Add(d);
        StatusText.Text = $"{l.Count} driver update(s) available."; Log(StatusText.Text);
    });

    async Task InstallSelected() {
        var sel = drivers.Where(d => d.Selected).ToList();
        if (sel.Count == 0) { MessageBox.Show("No drivers selected."); return; }
        var ok = MessageBox.Show($"Install {sel.Count} driver(s) from Windows Update?\n\n" + string.Join("\n", sel.Select(d => "- " + d.Title)) + "\n\nA restore point is created first. A restart may be needed.",
            "Confirm driver install", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        bool reboot = false;
        await Guard("Installing drivers... do not shut down.", async () => {
            var (lines, rb) = await Task.Run(() => DriverService.Install(sel.Select(d => d.Id).ToList()));
            foreach (var x in lines) Log(x); reboot = rb;
        });
        MessageBox.Show(reboot ? "Drivers installed. Restart to finish." : "Driver installation finished. See the Log page.", "PC Doctor");
        await Scan();
    }

    async void Scan_Click(object s, RoutedEventArgs e) => await Scan();
    async void Search_Click(object s, RoutedEventArgs e) => await DoSearch();
    async void Install_Click(object s, RoutedEventArgs e) => await InstallSelected();

    async void OneClick_Click(object s, RoutedEventArgs e) {
        await Scan(); NavDrv.IsChecked = true; await DoSearch();
        if (drivers.Count == 0) { MessageBox.Show("Windows Update has no driver updates for this PC. For vendor-specific drivers (touchpad, chipset) use your laptop maker's support page."); return; }
        await InstallSelected();
    }

    async void Backup_Click(object s, RoutedEventArgs e) => await Guard("Exporting drivers...", async () => {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PCDoctor-DriverBackup", DateTime.Now.ToString("yyyyMMdd-HHmm"));
        Directory.CreateDirectory(dir); await Sys.Cmd($"pnputil /export-driver * \"{dir}\"", Log); Log("Drivers exported to " + dir); StatusText.Text = "Drivers backed up to " + dir;
    });

    void Find_Click(object s, RoutedEventArgs e) {
        var q = ProblemBox.Text.ToLowerInvariant(); int hits = 0;
        foreach (var f in fixes) { f.Selected = q.Length > 0 && Regex.IsMatch(q, f.Fixer.Keys); if (f.Selected) hits++; }
        FixList.Items.Refresh();
        StatusText.Text = hits > 0 ? $"{hits} matching fix(es) selected. Review, then click Run selected fixes." : "No match. Try words like wifi, sound, slow, battery, printer.";
        if (Regex.IsMatch(q, "driver|touchpad|screen|display|graphics|camera")) StatusText.Text += " For drivers use One-click on the Dashboard.";
    }
    void Problem_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Find_Click(s, e); }

    async void RunFix_Click(object s, RoutedEventArgs e) {
        var sel = fixes.Where(f => f.Selected).ToList();
        if (sel.Count == 0) { MessageBox.Show("Tick at least one fix, or describe your problem and click Find a fix."); return; }
        if (MessageBox.Show("Run these fixes?\n\n" + string.Join("\n", sel.Select(f => "- " + f.Title)) + "\n\nA restore point is created first.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await Guard("Running fixes...", async () => {
            Log(await Task.Run(DriverService.RestorePoint));
            foreach (var f in sel) { Log("--- " + f.Title); try { await f.Fixer.Run(Log); } catch (Exception ex) { Log("Failed: " + ex.Message); } }
            Log("Selected fixes finished."); StatusText.Text = "Fixes finished. Restart if a fix needs it. See the Log page.";
        });
        await Scan();
    }

    void Export_Click(object s, RoutedEventArgs e) {
        bool redact = MessageBox.Show("Hide PC name and user name in the report?\n\nChoose Yes if you plan to share it.", "Export PDF report", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"PCDoctor-report-{DateTime.Now:yyyyMMdd-HHmm}.pdf" };
        if (dlg.ShowDialog() != true) return;
        try {
            PdfReport.Save(dlg.FileName, PdfReport.Build(findings, drivers, LogBox.Text, ScoreText.Text, redact));
            StatusText.Text = "Report saved: " + dlg.FileName; Log(StatusText.Text);
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        } catch (Exception ex) { MessageBox.Show("Could not save the report.\n" + ex.Message); }
    }

    async void BiosRead_Click(object s, RoutedEventArgs e) => await Guard("Reading BIOS settings...", async () => {
        try {
            var (rel, total) = await Task.Run(Bios.Read);
            bios.Clear(); foreach (var i in rel) bios.Add(i);
            StatusText.Text = $"Read {total} BIOS settings; showing {rel.Count} input-related. Red = disabled."; Log(StatusText.Text);
        } catch (Exception ex) {
            StatusText.Text = "Cannot read BIOS from Windows on this PC."; Log("BIOS read failed: " + ex.Message);
            MessageBox.Show(ex.Message + "\n\nRestart and press F2 / Del / F10 (varies by maker) to check the Touchpad / Internal Pointing Device / I2C HID setting manually.", "BIOS");
        }
    });

    string? AskPassword() {
        var pb = new PasswordBox { Margin = new Thickness(0, 8, 0, 12) }; string? res = null;
        var ok = new Button { Content = "Continue", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right };
        var w = new Window { Title = "BIOS password", Width = 380, SizeToContent = SizeToContent.Height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = "BIOS supervisor password (leave empty if none):" }, pb, ok } } };
        ok.Click += (_, _) => { res = pb.Password; w.DialogResult = true; };
        return w.ShowDialog() == true ? res : null;
    }

    async void BiosEnable_Click(object s, RoutedEventArgs e) {
        var it = (BiosItem)((Button)s).Tag;
        if (MessageBox.Show($"Change BIOS setting '{it.Name}' from {it.Value} to {it.EnableOption}?\nA restart is needed. Wrong BIOS changes can affect booting.", "Confirm BIOS change", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var pwd = AskPassword(); if (pwd == null) return;
        string? err = null;
        await Guard("Changing BIOS setting...", async () => err = await Task.Run(() => Bios.Enable(it, pwd)));
        if (err == null) { Log($"BIOS: {it.Name} set to {it.EnableOption}. Restart required."); MessageBox.Show("BIOS setting changed. Restart the PC for it to take effect.", "Done"); BiosRead_Click(s, e); }
        else { Log("BIOS change failed: " + err); MessageBox.Show("Could not change the setting.\n" + err, "Failed"); }
    }

    void VendorTool_Click(object s, RoutedEventArgs e) {
        var id = Bios.ToolId();
        if (id == null) { MessageBox.Show("No automatic vendor tool for this PC brand. Use 'Vendor support page'."); return; }
        if (MessageBox.Show($"Install {id} with winget? It is the official tool that downloads your model's exact drivers and BIOS updates.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (Bios.InstallTool(id)) Log("Started winget install of " + id); else MessageBox.Show("winget is not available. Use 'Vendor support page'.");
    }
    void VendorWeb_Click(object s, RoutedEventArgs e) => Bios.OpenSupportPage();

    // ---- theme ----
    static void SetTheme(bool dark) {
        var r = Application.Current.Resources;
        void B(string k, string c) => r[k] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
        if (dark) { B("Bg", "#0F1115"); B("Fg", "#E6E8EC"); B("Side", "#12151B"); B("CardBackground", "#171A21"); B("ButtonBackground", "#252A35"); B("Muted", "#8B93A1"); B("Fg2", "#C9CED8"); B("Hover", "#1A1F29"); B("Checked", "#252C3B"); B("Accent", "#4C8DFF"); }
        else { B("Bg", "#F4F6FA"); B("Fg", "#1B1F27"); B("Side", "#E9EDF4"); B("CardBackground", "#FFFFFF"); B("ButtonBackground", "#E3E8F0"); B("Muted", "#6B7380"); B("Fg2", "#3A4150"); B("Hover", "#DDE3EE"); B("Checked", "#D3DDF2"); B("Accent", "#2F6FEB"); }
    }
    void Theme_Click(object s, RoutedEventArgs e) { settings.Dark = !settings.Dark; SetTheme(settings.Dark); settings.Save(); }

    // ---- system tray + hourly background check ----
    void InitTray() {
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "PC Doctor", Visible = true };
        var m = new System.Windows.Forms.ContextMenuStrip();
        m.Items.Add("Open PC Doctor", null, (_, _) => ShowMain());
        m.Items.Add("Scan now", null, async (_, _) => { ShowMain(); await Scan(); });
        m.Items.Add("Exit", null, (_, _) => { exiting = true; tray!.Visible = false; tray.Dispose(); Application.Current.Shutdown(); });
        tray.ContextMenuStrip = m; tray.DoubleClick += (_, _) => ShowMain();
        Closing += (_, e) => { if (!exiting) { e.Cancel = true; Hide(); tray!.ShowBalloonTip(2000, "PC Doctor", "Still running in the tray. Right-click the icon to exit.", System.Windows.Forms.ToolTipIcon.Info); } };
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        t.Tick += async (_, _) => {
            if (IsVisible || busy) return;
            var p = (await Task.Run(Diagnostics.Scan)).Count(x => x.Level == Level.Problem);
            if (p > 0) tray!.ShowBalloonTip(4000, "PC Doctor", $"{p} problem(s) found. Open PC Doctor to fix them.", System.Windows.Forms.ToolTipIcon.Warning);
        };
        t.Start();
    }
    void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }

    // ---- updates (app + fix definitions) ----
    async void Update_Click(object s, RoutedEventArgs e) {
        if (!settings.UpdateUrl.StartsWith("https://")) {
            if (MessageBox.Show("No update feed is configured.\nSet \"UpdateUrl\" (an https link to your update.json) in the settings file, then restart.\n\nOpen the settings file now?", "Updates", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                settings.Save(); Process.Start(new ProcessStartInfo("notepad.exe", AppSettings.FilePath) { UseShellExecute = true });
            }
            return;
        }
        await CheckUpdates(false);
    }
    async Task CheckUpdates(bool silent) {
        try {
            var feed = await Updater.Fetch(settings.UpdateUrl);
            if (await Updater.UpdateFixes(feed)) Log("Fix definitions updated (restart to load them).");
            if (!Updater.IsNewer(feed)) { if (!silent) MessageBox.Show("You have the latest version.", "Updates"); return; }
            if (MessageBox.Show($"Version {feed.Version} is available.\n\n{feed.Notes}\n\nDownload and install now? PC Doctor will restart.", "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            StatusText.Text = "Downloading update...";
            var exe = await Updater.Download(feed.Url, feed.Sha256);
            Updater.ApplyAndRestart(exe); exiting = true; tray!.Visible = false; Application.Current.Shutdown();
        } catch (Exception ex) { Log("Update check failed: " + ex.Message); if (!silent) MessageBox.Show("Update failed.\n" + ex.Message, "Updates"); }
    }

    void Undo_Click(object s, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });

    void Share_Click(object s, RoutedEventArgs e) {
        var t = $"PC Doctor report {DateTime.Now:yyyy-MM-dd}\nWindows: {Environment.OSVersion.VersionString}\n\n" + string.Join("\n", findings.Select(f => $"{f.Level}: {f.Area} - {f.Check} - {f.Result}")) + "\n\n" + LogBox.Text;
        foreach (var x in new[] { Environment.MachineName, Environment.UserName }) if (x.Length > 0) t = t.Replace(x, "<redacted>");
        Clipboard.SetText(t); MessageBox.Show("Redacted report copied (PC name and user name removed).", "PC Doctor");
    }
}
