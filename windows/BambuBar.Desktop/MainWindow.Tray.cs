using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BambuBar.Core;
using BambuBar.Desktop.Services;

namespace BambuBar.Desktop;

public sealed partial class MainWindow
{
    private Window? quickPanel;
    private ContentControl? quickDetails;
    private TextBlock? quickNotice;
    private ComboBox? quickPicker;
    private bool quickFleet;
    private bool quickModal;
    private bool confirmDemoStop;
    private TrayVisual? lastTrayVisual;
    public event Action<TrayVisual>? TrayIndicatorChanged;

    public void ToggleQuickPanel()
    {
        if (quickPanel?.IsVisible == true) { quickPanel.Hide(); return; }
        ShowQuickPanel();
    }
    public void ShowQuickPanel()
    {
        if (Stopping) return;
        EnsureQuickPanel();
        selected = entries.FirstOrDefault(e => e.Config.Serial == settings.PrimarySerial) ?? selected ?? entries.FirstOrDefault();
        quickFleet = false;
        BuildQuickShell();
        var anchor = TrayPlacement.Cursor();
        var screen = anchor is { } point ? quickPanel!.Screens.ScreenFromPoint(point) : quickPanel!.Screens.Primary;
        screen ??= quickPanel!.Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            var scale = screen.Scaling;
            quickPanel!.Width = Math.Min(396, Math.Max(240, screen.WorkingArea.Width / scale - 24));
            quickPanel.Height = Math.Min(740, Math.Max(240, screen.WorkingArea.Height / scale - 24));
            var popupSize = new PixelSize((int)Math.Ceiling(quickPanel.Width * scale), (int)Math.Ceiling(quickPanel.Height * scale));
            quickPanel.Position = TrayPlacement.Place(anchor ?? new PixelPoint(screen.WorkingArea.Right, screen.WorkingArea.Bottom),
                screen.WorkingArea, popupSize, (int)Math.Ceiling(10 * scale));
        }
        quickPanel!.Show(); quickPanel.Activate();
    }
    private void EnsureQuickPanel()
    {
        if (quickPanel is not null) return;
        quickPanel = new Window
        {
            Title = L("Menu bar"), Width = 396, Height = 740,
            CanResize = false, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent, Topmost = true, FontFamily = FontFamily,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        RoundedWindowRegion.Attach(quickPanel);
        quickPanel.Deactivated += (_, _) =>
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (quickPanel.IsActive || quickModal || quickPicker?.IsDropDownOpen == true || Program.Arguments.Contains("--smoke-test")) return;
                quickPanel.Hide();
            }, TimeSpan.FromMilliseconds(120));
        };
        quickPanel.KeyDown += (_, e) => { if (e.Key == Key.Escape && !quickModal) { quickPanel.Hide(); e.Handled = true; } };
        quickPanel.AddHandler(KeyDownEvent, HandleQuickShortcuts, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        quickPanel.Closing += (_, e) => { if (!Stopping) { e.Cancel = true; quickPanel.Hide(); } };
    }
    private void BuildQuickShell()
    {
        if (quickPanel is null) return;
        quickPanel.TransparencyBackgroundFallback = Brush(Dark ? "#1B2520" : "#F0F5F1");
        quickPanel.Title = L("Menu bar");
        quickPicker = new ComboBox { ItemsSource = entries, SelectedItem = selected, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 34 };
        quickPicker.SelectionChanged += (_, _) =>
        {
            selected = quickPicker.SelectedItem as Entry; quickFleet = false;
            UpdateQuickPanel(); BuildShell();
        };
        var all = ActionButton("grid", "", () => { quickFleet = !quickFleet; UpdateQuickPanel(); });
        all.Padding = new Thickness(10, 8); ToolTip.SetTip(all, L("All Printers"));
        var header = Columns("*,Auto", quickPicker, all);
        quickDetails = new ContentControl();
        var scroller = new ScrollViewer { Content = quickDetails, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        quickNotice = Text(notice.Text ?? "", 11, Muted); quickNotice.IsVisible = notice.IsVisible;
        var open = ActionButton("arrow", L("Open Bambu Bar"), () => OpenFromQuickPanel("printer"));
        var preferences = ActionButton("settings", "", () => OpenFromQuickPanel("settings"));
        preferences.Padding = new Thickness(11, 9); ToolTip.SetTip(preferences, L("Settings"));
        var footer = Stack(quickNotice, Columns("*,Auto", open, preferences)); footer.Spacing = 8;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12, Margin = new Thickness(14) };
        layout.Children.Add(header); Grid.SetRow(scroller, 1); layout.Children.Add(scroller); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        quickPanel.Content = new Border { Child = layout, Background = Brush(Dark ? "#F0192821" : "#E8F0F6F1"),
            BorderBrush = Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(RoundedWindowRegion.PanelRadius), ClipToBounds = true };
        UpdateQuickPanel();
    }
    private void OpenFromQuickPanel(string target)
    {
        quickPanel?.Hide(); Navigate(target); Show(); Activate();
    }
    private void UpdateQuickPanel()
    {
        if (Stopping || quickDetails is null) return;
        if (quickNotice is not null) { quickNotice.Text = notice.Text; quickNotice.IsVisible = notice.IsVisible; }
        if (busy || quickModal || interactiveViews.Values.Any(v => v.IsInteracting) || quickPicker?.IsDropDownOpen == true) return;
        quickDetails.Content = quickFleet ? QuickFleet() : QuickPrinter();
    }
    private Control QuickFleet()
    {
        var list = Stack(Kicker(L("All Printers")));
        foreach (var entry in entries.OrderBy(e => e.Status?.State is JobState.Failed or JobState.Paused ? 0 : 1).ThenBy(e => e.Status?.RemainingMinutes ?? double.MaxValue))
        {
            var e = entry;
            var summary = Stack(Text(e.Config.Name, 14), Text($"{PhaseText(e)} · {Value(e.Status?.Progress)}% · {Remaining(e.Status)}", 11, Muted));
            summary.Spacing = 5;
            var choose = Button("", () => { selected = e; quickFleet = false; if (quickPicker is not null) quickPicker.SelectedItem = e; UpdateQuickPanel(); BuildShell(); });
            choose.Content = Columns("65,*", PrinterImage(e, 80), summary); choose.HorizontalAlignment = HorizontalAlignment.Stretch;
            list.Children.Add(choose);
        }
        if (entries.Count == 0) list.Children.Add(QuickEmpty());
        return list;
    }
    private Control QuickEmpty() => Card(Stack(Text(L("Your Bambu Lab printer, one click away in the menu bar."), 20),
        Text(L("Open Bambu Bar, pick your printer and enter its access code. Progress, temperatures and filament show up here."), 12, Muted),
        ActionButton("plus", L("Add Printer…"), () => OpenFromQuickPanel("settings"), "primary")));

    private Control QuickPrinter()
    {
        if (selected is not { } entry) return QuickEmpty();
        var s = entry.Status;
        var model = PrinterModel.Find(entry.Config.ModelCode, entry.Config.Serial);
        var statusColor = entry.Phase == ConnectionPhase.Online ? StatusColor(s) : Muted;
        var title = Text(s?.Job is { Length: > 0 } job ? job : L("Ready to print"), 18);
        title.MaxLines = 2; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var metrics = Stack(Badge(PhaseText(entry), statusColor), Text($"{Value(s?.Progress)}%", 36),
            Label("clock", Remaining(s), Ink, 12), Text($"{s?.Layer?.ToString() ?? "—"}/{s?.TotalLayers?.ToString() ?? "—"} {L("layers")}", 10, Muted));
        metrics.Spacing = 10; metrics.VerticalAlignment = VerticalAlignment.Center;
        var hero = Stack(title, Columns("135,*", PrinterImage(entry, 165, true), metrics),
            new ProgressBar { Minimum = 0, Maximum = 100, Value = s?.Progress ?? 0, Foreground = statusColor, Height = 5, Background = Hairline });
        var heroCard = Card(hero); heroCard.Padding = new Thickness(15); hero.Spacing = 10;
        var result = Stack(heroCard); result.Spacing = 12;
        if (s is not null) result.Children.Add(Text(PrintStages.Describe(s, settings.Language), 11, Muted));
        if (entry.Phase != ConnectionPhase.Online)
        {
            result.Children.Add(Card(Stack(Text(PhaseText(entry), 13), Text(L("Bambu Bar keeps trying in the background."), 11, Muted),
                ActionButton("settings", L("Change…"), () => OpenFromQuickPanel("settings")))));
        }
        var temperatures = new List<Control>();
        void TemperatureTile(string label, Temperature? t)
        {
            var tile = Stack(Text(label, 10, Muted), Text($"{Value(t?.Current)}°", 22)); tile.Spacing = 5;
            var card = Card(tile); card.Padding = new Thickness(12);
            ToolTip.SetTip(card, $"{L("Heating")}: {Value(t?.Target)} °C"); temperatures.Add(card);
        }
        TemperatureTile(s?.SecondNozzle is null ? L("Nozzle") : L("Right"), s?.Nozzle);
        TemperatureTile(L("Bed"), s?.Bed);
        if (s?.SecondNozzle is not null) TemperatureTile(L("Left"), s.SecondNozzle);
        if (model?.Chamber != false) TemperatureTile(L("Chamber"), s?.Chamber);
        result.Children.Add(Columns(string.Join(',', temperatures.Select(_ => "*")), temperatures.ToArray()));
        var online = entry.Phase == ConnectionPhase.Online && s is not null && !busy;
        var paused = s?.State == JobState.Paused;
        var canControl = online && !s!.ControlsLocked && s.State is JobState.Printing or JobState.Preparing or JobState.Paused;
        var pause = ActionButton(paused ? "play" : "pause", paused ? L("Resume") : L("Pause"),
            () => _ = QuickCommand(entry, paused ? "resume" : "pause"), enabled: canControl);
        var stop = ActionButton("stop", L("Stop"), () => _ = QuickCommand(entry, "stop"), "danger", canControl);
        pause.HorizontalAlignment = stop.HorizontalAlignment = HorizontalAlignment.Stretch;
        pause.HorizontalContentAlignment = stop.HorizontalContentAlignment = HorizontalAlignment.Center;
        result.Children.Add(Columns("*,*", pause, stop));
        if (s?.ControlsLocked == true) result.Children.Add(Text(L("Turn on LAN Only and Developer Mode on the printer to control prints from Bambu Bar."), 11, Muted));
        var light = ActionButton("light", L("Light"), () => _ = QuickCommand(entry, "ledctrl", light: s?.Light != true), enabled: online && model?.Light != false);
        ToolTip.SetTip(light, s?.Light == true ? L("Turn Chamber Light Off") : L("Turn Chamber Light On"));
        var speed = Button($"{L("Speed")} · {new[] { "50%", "100%", "124%", "166%" }[Math.Clamp((s?.Speed ?? 2) - 1, 0, 3)]}", () => _ = QuickSpeed(entry), online && !s!.ControlsLocked);
        light.HorizontalAlignment = speed.HorizontalAlignment = HorizontalAlignment.Stretch;
        result.Children.Add(Columns("Auto,*", light, speed));
        if (s?.Issues.FirstOrDefault() is { } issue) result.Children.Add(ErrorCard(issue));
        foreach (var unit in s?.Ams ?? []) result.Children.Add(AmsCard(unit, true));
        if (s?.External is { Loaded: true } spool) result.Children.Add(SpoolCard(spool));
        return result;
    }
    private async Task QuickCommand(Entry entry, string command, int? speed = null, bool? light = null)
    {
        if (busy || quickModal) return;
        quickModal = command == "stop";
        try { await Send(entry, command, speed, light, quickPanel); }
        finally { quickModal = false; UpdateQuickPanel(); if (quickPanel?.IsVisible == true) quickPanel.Activate(); }
    }
    private async Task QuickSpeed(Entry entry)
    {
        if (busy || quickModal || quickPanel is null) return;
        quickModal = true;
        try
        {
            var dialog = new Window { Title = L("Speed"), Width = 300, Height = 280, CanResize = false,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var options = Stack(Text(L("Speed"), 18)); options.Spacing = 8; options.Margin = new Thickness(18);
            var names = SpeedNames();
            for (var i = 0; i < names.Length; i++)
            { var level = i + 1; var button = Button(names[i], () => dialog.Close(level)); button.HorizontalAlignment = HorizontalAlignment.Stretch; options.Children.Add(button); }
            dialog.Content = options;
            var choice = await dialog.ShowDialog<int?>(quickPanel);
            if (choice is not null) await Send(entry, "print_speed", speed: choice, owner: quickPanel);
        }
        finally { quickModal = false; UpdateQuickPanel(); if (quickPanel.IsVisible) quickPanel.Activate(); }
    }
    private void UpdateTrayIndicator(Entry? primary)
    {
        var value = new TrayVisual(primary?.Phase ?? ConnectionPhase.Idle, primary?.Status?.State,
            (int)Math.Clamp(primary?.Status?.Progress ?? 0, 0, 100), primary?.Status?.Canceled == true);
        if (value == lastTrayVisual) return;
        lastTrayVisual = value; TrayIndicatorChanged?.Invoke(value);
    }
    private async Task PreviewQuickPanel()
    {
        EnsureQuickPanel();
        foreach (var theme in new[] { "light", "dark" })
        {
            settings.Theme = theme; ApplyTheme();
            BuildQuickShell(); quickPanel!.Show();
            await Task.Delay(150);
            RoundedWindowRegion.Verify(quickPanel);
            using var bitmap = new RenderTargetBitmap(new PixelSize(396, 740), new Vector(96, 96));
            bitmap.Render(quickPanel); bitmap.Save($"build/windows/preview-menubar-{theme}.png");
        }
        quickFleet = true; UpdateQuickPanel(); await Task.Delay(150);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(396, 740), new Vector(96, 96)))
        { bitmap.Render(quickPanel!); bitmap.Save("build/windows/preview-menubar-fleet.png"); }
        quickPanel!.Hide(); quickFleet = false;
        await VerifyQuickPanel();
    }
    private async Task VerifyQuickPanel()
    {
        if (!demo) throw new InvalidOperationException("Quick panel checks require demo mode.");
        foreach (var area in new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(-1920, -200, 1920, 1400) })
        foreach (var scale in new[] { 1d, 1.5d })
        foreach (var anchor in new[] { new PixelPoint(area.X - 10, area.Y + 100), new PixelPoint(area.Right + 10, area.Bottom + 20), new PixelPoint(area.X + 50, area.Y - 20) })
        {
            var size = new PixelSize((int)(396 * scale), Math.Min((int)(740 * scale), area.Height - 24));
            var placed = TrayPlacement.Place(anchor, area, size, 10);
            if (placed.X < area.X || placed.Y < area.Y || placed.X + size.Width > area.Right || placed.Y + size.Height > area.Bottom)
                throw new InvalidOperationException("Tray placement escaped the monitor working area.");
        }
        var entry = entries.First(); selected = entry;
        var original = entry.Demo!;
        var mainWasVisible = IsVisible;
        try
        {
            Hide(); ShowQuickPanel();
            if (quickPanel?.IsVisible != true || IsVisible) throw new InvalidOperationException("Quick panel requires the hidden main window.");
            ToggleQuickPanel();
            if (quickPanel.IsVisible) throw new InvalidOperationException("Second tray click did not hide panel.");
            ShowQuickPanel();
            await Task.Delay(150);
            RoundedWindowRegion.Verify(quickPanel);
            if (OperatingSystem.IsWindows())
            {
                var width = quickPanel.Width;
                quickPanel.Width = width - 24;
                await Task.Delay(100);
                RoundedWindowRegion.Verify(quickPanel);
                quickPanel.Width = width;
                await Task.Delay(100);
                RoundedWindowRegion.Verify(quickPanel);
            }
            await QuickCommand(entry, "pause");
            if (entry.Status?.State != JobState.Paused) throw new InvalidOperationException("Tray pause failed.");
            await QuickCommand(entry, "resume");
            if (entry.Status?.State != JobState.Printing) throw new InvalidOperationException("Tray resume failed.");
            await QuickCommand(entry, "print_speed", speed: 3);
            if (entry.Status?.Speed != 3) throw new InvalidOperationException("Tray speed failed.");
            await QuickCommand(entry, "ledctrl", light: false);
            if (entry.Status?.Light != false) throw new InvalidOperationException("Tray light failed.");
            confirmDemoStop = true;
            await QuickCommand(entry, "stop");
            if (entry.Status?.State != JobState.Failed || IsVisible) throw new InvalidOperationException("Tray stop confirmation failed with a hidden main window.");
            Console.WriteLine("Quick panel: DPI placement, toggle, hidden main window, controls and owned confirmation passed.");
        }
        finally
        {
            confirmDemoStop = false; entry.Demo = original; quickPanel?.Hide();
            if (mainWasVisible) Show();
        }
    }
}
