using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using BambuBar.Core;
using BambuBar.Desktop.Services;

namespace BambuBar.Desktop;

public sealed partial class MainWindow : Window
{
    private sealed class Entry(PrinterConfig config, PrinterSession? session = null)
    {
        public PrinterConfig Config = config;
        public PrinterSession? Session = session;
        public PrinterStatus? Demo;
        public PrinterStatus? Status => Session?.Status ?? Demo;
        public ConnectionPhase Phase => Session?.Phase ?? ConnectionPhase.Online;
        public override string ToString() => Config.Name;
    }
    private readonly bool demo = Program.Arguments.Contains("--demo");
    private readonly bool emulator = Program.Arguments.Contains("--emulator");
    private readonly SettingsStore store;
    private readonly Settings settings;
    private readonly List<Entry> entries = [];
    private readonly Dictionary<string, Bitmap> images = [];
    private readonly ContentControl detail = new();
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24, 10), IsVisible = false };
    private readonly DispatcherTimer timer;
    private Entry? selected;
    private string page = "printer";
    private bool busy;
    private bool storageFailed;
    private ComboBox? speedPicker;
    private Task? stopTask;
    private bool smokeStarted;
    public bool Stopping { get; private set; }
    public event Action<string>? TrayTextChanged;
    public event Action? LanguageChanged;
    public string L(string key, params object?[] args) => TextCatalog.Translate(key, settings.Language, args).Replace("**", "");
    private bool Dark => ActualThemeVariant == ThemeVariant.Dark;
    private IBrush CardBrush => Brush(Dark ? "#782D3231" : "#ACFFFFFF");
    private IBrush Muted => Brush(Dark ? "#A1ADA8" : "#66746D");
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#42D392"));

    public MainWindow()
    {
        store = new(demo || emulator);
        string? startupError = null;
        try { settings = store.Load(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        { settings = new(); storageFailed = true; startupError = "settings"; }
        settings.Language = TextCatalog.Normalize(settings.Language); TextCatalog.Language = settings.Language;
        Title = "Bambu Bar"; Width = 1120; Height = 860; MinWidth = 880; MinHeight = 640;
        FontFamily = new FontFamily("Segoe UI, Inter, DejaVu Sans");
        TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Mica, WindowTransparencyLevel.Blur];
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 36;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://BambuBar/Assets/app.ico")));
        ApplyTheme();
        InitializeFeatures();
        if (demo) AddDemo();
        else foreach (var saved in settings.Printers)
        {
            try { AddSession(saved.Config, store.Unprotect(saved.ProtectedCode)); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or PlatformNotSupportedException)
            { startupError = "credentials"; }
        }
        selected = entries.FirstOrDefault(e => e.Config.Serial == settings.PrimarySerial) ?? entries.FirstOrDefault();
        StartDiscovery();
        if (selected is null) page = "settings";
        BuildShell();
        if (startupError is not null) Notify(startupError == "settings"
            ? L("Error")
            : L("Check the access code on the printer's screen (Settings → Network) and enter it again."));
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            UpdateSidebar();
            if (!busy && !interactiveViews.Values.Any(v => v.IsInteracting) && speedPicker?.IsDropDownOpen != true && page is "printer" or "filament" or "fleet") RenderPage();
            var primary = entries.FirstOrDefault(e => e.Config.Serial == settings.PrimarySerial) ?? selected;
            var name = primary?.Config.Name ?? "";
            if (name.Length > 40) name = name[..40] + "…";
            TrayTextChanged?.Invoke(primary is null ? "Bambu Bar" : $"Bambu Bar · {name} · {PhaseText(primary)} · {Value(primary.Status?.Progress)}% · {Remaining(primary.Status)}");
            UpdateTrayIndicator(primary);
            if (quickPanel?.IsVisible == true) UpdateQuickPanel();
        };
        timer.Start();
        ActualThemeVariantChanged += (_, _) => BuildShell();
        if (Program.Arguments.Contains("--smoke-test")) Opened += async (_, _) =>
        {
            if (smokeStarted) return;
            smokeStarted = true;
            try
            {
                Directory.CreateDirectory("build/windows");
                foreach (var theme in new[] { "light", "dark" })
                foreach (var language in new[] { "en", "pt-BR" })
                foreach (var section in new[] { "printer", "filament", "fleet", "settings" })
                {
                    settings.Theme = theme; settings.Language = language; page = section; ApplyTheme(); BuildShell();
                    await Task.Delay(150);
                    if (language == "pt-BR")
                    {
                        using var shot = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96));
                        shot.Render(this); shot.Save($"build/windows/preview-{section}-{theme}.png");
                    }
                }
                settings.Theme = "dark"; settings.Language = "pt-BR"; page = "printer"; ApplyTheme(); BuildShell();
                Width = 880; Height = 760; BuildShell();
                await Task.Delay(150);
                using (var compact = new RenderTargetBitmap(new PixelSize(880, 760), new Vector(96, 96)))
                { compact.Render(this); compact.Save("build/windows/preview-compact.png"); }
                Width = 1120; Height = 860; BuildShell();
                await Task.Delay(300);
                Directory.CreateDirectory("build/windows");
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96));
                bitmap.Render(this); bitmap.Save("build/windows/preview.png");
                await PreviewFeatures();
                await PreviewDiscovery();
                await PreviewQuickPanel();
                await StopAsync();
                ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Shutdown(1);
            }
        };
    }
    private void AddDemo()
    {
        var catalog = new Catalogs(settings.Language);
        var doc = new ReportDocument();
        doc.Apply(JsonNode.Parse("""
            {"print":{"msg":0,"gcode_state":"RUNNING","subtask_name":"Planetary Gearbox","mc_percent":64,"mc_remaining_time":62,"layer_num":192,"total_layer_num":300,"nozzle_temper":220,"nozzle_target_temper":220,"bed_temper":55,"bed_target_temper":55,"chamber_temper":34,"stg_cur":7,"task_id":"demo1","wifi_signal":"-52dBm","sdcard":true,"stat":"00000000","nozzle_type":"HS01","nozzle_diameter":"0.4","cooling_fan_speed":15,"big_fan1_speed":6,"big_fan2_speed":5,"heatbreak_fan_speed":15,"spd_lvl":2,"lights_report":[{"node":"chamber_light","mode":"on"}],"ams":{"tray_now":0,"ams":[{"id":"0","humidity_raw":24,"humidity":2,"dry_time":46,"temp":27,"tray":[{"id":"0","tray_type":"PLA","tray_color":"61C680FF","tray_info_idx":"GFA01","cols":["61C680FF","C8B46AFF"],"nozzle_temp_min":190,"nozzle_temp_max":230,"tray_weight":1000,"remain":81,"tag_uid":"1234","tray_id_name":"A01-G1"},{"id":"1","tray_type":"PETG","tray_color":"1F79E5FF","tray_weight":1000,"remain":42},{"id":"2"},{"id":"3","tray_type":"PLA","tray_color":"F330F9FF","tray_info_idx":"GFSNL02","remain":75}]}]}}}
            """)!.AsObject());
        var status = ReportParser.Parse(doc, catalog, PrinterModel.Find("N7"));
        entries.Add(new(new("Workshop P2S", "127.0.0.1", "22EDEMO00001", "N7")) { Demo = status });
        entries.Add(new(new("A1 Desk", "127.0.0.1", "039DEMO00002", "N2S")) { Demo = status with { State = JobState.Paused, Progress = 28, Chamber = null, Light = null } });
        entries.Add(new(new("X1C Lab", "127.0.0.1", "00MDEMO00003", "BL-P001")) { Demo = status with { State = JobState.Finished, Progress = 100, RemainingMinutes = 0 } });
    }
    private void AddSession(PrinterConfig config, string code)
    {
        var session = new PrinterSession(config, code, new Catalogs(settings.Language), emulator);
        var entry = new Entry(config, session); entries.Add(entry); ObserveSession(entry); session.Start();
    }
    private void ApplyTheme() => Application.Current!.RequestedThemeVariant = settings.Theme switch
    { "dark" => ThemeVariant.Dark, "light" => ThemeVariant.Light, _ => ThemeVariant.Default };
    private void Notify(string text) { notice.Text = text; notice.IsVisible = !string.IsNullOrEmpty(text); }
    private void Save()
    {
        if (storageFailed) throw new IOException(L("Error"));
        store.Save(settings);
    }
    private string StateText(JobState? state) => state switch
    {
        JobState.Printing => L("Printing"), JobState.Preparing => L("Preparing"),
        JobState.Paused => L("Paused"), JobState.Finished => L("Finished"),
        JobState.Failed => L("Failed"), JobState.Idle => L("Ready"), _ => "—"
    };
    private string PhaseText(Entry entry) => entry.Phase switch
    {
        ConnectionPhase.Online => entry.Status is { } s ? L(s.StateKey) : "—", ConnectionPhase.Connecting => L("Connecting…"),
        ConnectionPhase.Unauthorized => L("Access code rejected"),
        ConnectionPhase.Certificate => L("Certificate not trusted"),
        _ => L("Offline")
    };
    private static string Value(double? v) => v?.ToString("0", CultureInfo.CurrentCulture) ?? "—";
    private string Remaining(PrinterStatus? s)
    {
        if (s?.Active != true) return "—";
        var online = entries.FirstOrDefault(e => ReferenceEquals(e.Status, s))?.Phase == ConnectionPhase.Online;
        if (s?.ProjectedMinutes(DateTimeOffset.UtcNow, online) is not { } minutes) return L("Estimating…");
        if (minutes < 1) return L("<1m");
        var total = (int)Math.Ceiling(Math.Min(minutes, 525600));
        return Duration(total);
    }
    private IBrush StateColor(JobState? state) => state switch {
        JobState.Paused or JobState.Preparing => Brush("#EAB66A"), JobState.Failed => Brush("#F27E87"), _ => Green };
    private IBrush StatusColor(PrinterStatus? status) => status?.Canceled == true ? Muted : StateColor(status?.State);
    private Control SettingsPage()
    {
        var languageCodes = TextCatalog.Languages.ToArray();
        var languages = new ComboBox { ItemsSource = languageCodes.Select(c => CultureInfo.GetCultureInfo(c).NativeName).ToArray(), SelectedIndex = Array.IndexOf(languageCodes, settings.Language) };
        languages.SelectionChanged += (_, _) => Guard(() => { if (languages.SelectedIndex < 0) return; settings.Language = languageCodes[languages.SelectedIndex]; TextCatalog.Language = settings.Language; Save(); foreach (var e in entries) e.Session?.ChangeLanguage(settings.Language); BuildShell(); });
        var themes = new ComboBox { ItemsSource = new[] { L("System"), "☀", "☾" }, SelectedIndex = settings.Theme switch { "light" => 1, "dark" => 2, _ => 0 } };
        themes.SelectionChanged += (_, _) => Guard(() => { settings.Theme = themes.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" }; Save(); ApplyTheme(); BuildShell(); });
        languages.HorizontalAlignment = HorizontalAlignment.Stretch;
        themes.HorizontalAlignment = HorizontalAlignment.Stretch;
        var result = Stack(Heading(L("Settings"), L("Bambu Bar")),
            Kicker(L("Settings")), Card(Columns("*,*", Stack(Label("settings", L("Language")), languages), Stack(Label("light", L("Showing")), themes))),
            Kicker(L("Printers")));
        result.Spacing = 20;
        result.Children.Insert(3, DiscoverySection(printer => detail.Content = Editor(null, printer)));
        result.Children.Add(NotificationSettings());
        result.Children.Add(ShortcutSettings());
        foreach (var saved in settings.Printers)
        {
            var config = saved.Config;
            result.Children.Add(Card(Stack(Text(config.Name, 18), Text($"{config.Host}:{config.Port} · {config.Serial}", 12, Muted),
                Row(Button(L("Change…"), () => detail.Content = Editor(config)),
                    Button(L("Shown in the menu bar") + (settings.PrimarySerial == config.Serial ? " ✓" : ""), () => Guard(() => { settings.PrimarySerial = config.Serial; Save(); RenderPage(); })),
                    Button(L("Forget"), () => _ = Remove(config))))));
        }
        result.Children.Add(Button(L("Add Printer…"), () => detail.Content = Editor(null), !demo && !storageFailed));
        result.Children.Add(Text(L("Bambu Lab, Bambu Studio and P2S are trademarks of Bambu Lab. Bambu Bar is an independent project, not affiliated with or endorsed by Bambu Lab."), 12, Muted));
        return result;
    }
    private Control Editor(PrinterConfig? original, DiscoveredPrinter? discovered = null)
    {
        var initial = original ?? discovered?.Config;
        var name = new TextBox { Text = initial?.Name, Watermark = L("Name") };
        var host = new TextBox { Text = initial?.Host, Watermark = "192.168.1.100" };
        var port = new TextBox { Text = (initial?.Port ?? 8883).ToString(), Watermark = "8883" };
        var serial = new TextBox { Text = initial?.Serial, Watermark = L("Serial number") };
        var code = new TextBox { PasswordChar = '●', Watermark = original is null ? L("8-character access code") : L("Optional") };
        var models = new ComboBox { ItemsSource = new[] { L("Automatic") }.Concat(PrinterModel.All.Select(m => m.Name)).ToArray(),
            SelectedIndex = initial is null ? 0 : Array.FindIndex(PrinterModel.All, m => m.Code == initial.ModelCode) + 1 };
        var saveButton = new Button { Content = L("Connect") };
        saveButton.Classes.Add("primary");
        saveButton.Click += (_, _) => _ = SavePrinter();
        async Task SavePrinter()
        {
            saveButton.IsEnabled = false;
            try
            {
                if (!int.TryParse(port.Text, out var portValue)) throw new ArgumentException(L("Network"));
                if (string.IsNullOrWhiteSpace(serial.Text))
                {
                    var address = host.Text?.Trim() ?? "";
                    Notify(L("Finding Printer…"));
                    var identified = await PrinterIdentity.ReadSerialAsync(address, portValue, discoveryLifetime.Token, emulator);
                    if (host.Text?.Trim() != address || port.Text != portValue.ToString() || !string.IsNullOrWhiteSpace(serial.Text))
                        throw new OperationCanceledException(L("Try Again"));
                    serial.Text = identified;
                    if (string.IsNullOrWhiteSpace(name.Text)) name.Text = PrinterModel.Find("", identified)?.Name ?? L("Bambu Lab printer");
                }
                if (string.IsNullOrWhiteSpace(name.Text)) name.Text = PrinterModel.Find("", serial.Text?.Trim() ?? "")?.Name ?? L("Bambu Lab printer");
                var config = new PrinterConfig(name.Text?.Trim() ?? "", host.Text?.Trim() ?? "", serial.Text?.Trim().ToUpperInvariant() ?? "",
                    models.SelectedIndex > 0 ? PrinterModel.All[models.SelectedIndex - 1].Code : "", portValue);
                config.Validate(emulator);
                if (settings.Printers.Any(p => p.Config.Serial == config.Serial && p.Config.Serial != original?.Serial))
                    throw new ArgumentException(L("Connected"));
                var existing = original is null ? null : settings.Printers.FirstOrDefault(p => p.Config.Serial == original.Serial);
                var reuseCode = string.IsNullOrEmpty(code.Text) && existing is not null && config.Serial.Equals(existing.Config.Serial, StringComparison.OrdinalIgnoreCase);
                var secret = reuseCode ? store.Unprotect(existing!.ProtectedCode) : code.Text ?? "";
                if (secret.Length != 8 || !secret.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException(L("8-character access code"));
                // Construct first, so validation errors cannot remove the previous configuration.
                var session = new PrinterSession(config, secret, new Catalogs(settings.Language), emulator);
                var replacement = new SavedPrinter(config, store.Protect(secret));
                var previous = settings.Printers.ToList(); var previousPrimary = settings.PrimarySerial;
                settings.Printers.RemoveAll(p => p.Config.Serial == original?.Serial); settings.Printers.Add(replacement);
                if (settings.PrimarySerial is null || settings.PrimarySerial == original?.Serial) settings.PrimarySerial = config.Serial;
                try { Save(); } catch { settings.Printers = previous; settings.PrimarySerial = previousPrimary; await session.DisposeAsync(); throw; }
                var old = entries.FirstOrDefault(e => e.Config.Serial == original?.Serial);
                if (old is not null) { if (old.Session is not null) await old.Session.DisposeAsync(); entries.Remove(old); }
                var entry = new Entry(config, session); entries.Add(entry); selected = entry; ObserveSession(entry); session.Start();
                page = "printer"; BuildShell(); Notify(L("Connecting…"));
            }
            catch (Exception ex) { Notify(L("%@: %@", L("Error"), ex.Message)); }
            finally { saveButton.IsEnabled = true; }
        }
        var editor = Stack(Text(original is null ? L("Add Printer…") : L("Change…"), 28),
            Card(Stack(Text(L("Name"), 12, Muted), name, Text(L("IP address"), 12, Muted), host,
                Text(L("Network"), 12, Muted), port, Text(original is null ? L("%@ · %@", L("Serial number"), L("Optional")) : L("Serial number"), 12, Muted), serial,
                Text(L("Model"), 12, Muted), models, code,
                Text(L("Enter the printer's access code."), 13, Muted),
                Text(L("Only the IP address is needed: it's on the printer's screen under Settings → Network. Bambu Bar reads the serial number from the printer."), 12, Muted),
                Row(saveButton, Button(L("Cancel"), RenderPage)))));
        if (original is null) editor.Children.Insert(1, DiscoverySection(found =>
        {
            if (!string.Equals(serial.Text, found.Serial, StringComparison.OrdinalIgnoreCase)) code.Text = "";
            name.Text = found.Name; host.Text = found.Host; port.Text = found.Port.ToString(); serial.Text = found.Serial;
            models.SelectedIndex = Array.FindIndex(PrinterModel.All, m => m.Code == found.ModelCode) + 1;
            code.Focus();
        }));
        return editor;
    }
    private async Task Remove(PrinterConfig config)
    {
        if (!await Confirm(L("Forget Printer…"), config.Name)) return;
        try
        {
            var previous = settings.Printers.ToList(); var previousPrimary = settings.PrimarySerial;
            settings.Printers.RemoveAll(p => p.Config.Serial == config.Serial);
            if (settings.PrimarySerial == config.Serial) settings.PrimarySerial = settings.Printers.FirstOrDefault()?.Config.Serial;
            try { Save(); } catch { settings.Printers = previous; settings.PrimarySerial = previousPrimary; throw; }
            var entry = entries.FirstOrDefault(e => e.Config.Serial == config.Serial);
            if (entry?.Session is not null) await entry.Session.DisposeAsync();
            if (entry is not null) entries.Remove(entry);
            if (selected == entry) selected = entries.FirstOrDefault(); BuildShell();
        }
        catch (Exception ex) { Notify(ex.Message); }
    }
    private async Task<bool> Confirm(string title, string text, Window? owner = null)
    {
        var dialog = new Window { Title = title, Width = 390, Height = 200, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = Stack(Text(title, 21), Text(text), Row(Button(L("Done"), () => dialog.Close(true)), Button(L("Cancel"), () => dialog.Close(false))));
        content.Margin = new Thickness(24); dialog.Content = content;
        if (confirmDemoStop && demo) dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() => dialog.Close(true));
        return await dialog.ShowDialog<bool>(owner ?? this);
    }
    private async Task Send(Entry entry, string command, int? speed = null, bool? light = null, Window? owner = null)
    {
        if (busy) return;
        if (command == "stop" && !await Confirm(L("Stop this print?"), L("The print can't be resumed after it's stopped."), owner)) return;
        busy = true;
        try
        {
            Notify(L("Waiting for the printer"));
            if (entry.Session is not null) await entry.Session.CommandAsync(command, speed, light);
            else if (entry.Demo is { } s) entry.Demo = command switch {
                "pause" => s with { State = JobState.Paused, StageId = 16 }, "resume" => s with { State = JobState.Printing, StageId = 0 },
                "stop" => s with { State = JobState.Failed, Canceled = true, StageId = 255 }, "print_speed" => s with { Speed = speed }, "ledctrl" => s with { Light = light }, _ => s };
            Notify(L("Done"));
        }
        catch (TimeoutException) { Notify(L("The printer didn't answer.")); }
        catch (Exception ex) { Notify(L("%@: %@", L("Error"), ex.Message)); }
        finally { busy = false; RenderPage(); }
    }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { Notify(ex.Message); } }
    public Task StopAsync() => stopTask ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        Stopping = true; timer.Stop();
        foreach (var view in interactiveViews.Values) view.StopAnimation();
        desktopNotifier?.Dispose();
        await discoveryLifetime.CancelAsync();
        if (discovery is not null) await discovery.DisposeAsync();
        quickPanel?.Close();
        foreach (var entry in entries) if (entry.Session is not null) await entry.Session.DisposeAsync();
        foreach (var image in images.Values) image.Dispose();
    }
}
