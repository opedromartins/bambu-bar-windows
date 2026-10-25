using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BambuBar.Core;
using BambuBar.Desktop.Services;

namespace BambuBar.Desktop;

public sealed partial class MainWindow
{
    private string Duration(int minutes) => minutes >= 1440 ? L("%lldd %lldh", minutes / 1440, minutes % 1440 / 60) : minutes >= 60 ? L("%lldh %@m", minutes / 60, (minutes % 60).ToString("00")) : L("%lldm", minutes);
    private string[] SpeedNames() => new[] { ("Silent", 50), ("Standard", 100), ("Sport", 124), ("Ludicrous", 166) }
        .Select(s => L("%@ · %@", L(s.Item1), L("%lld%%", s.Item2))).ToArray();
    private readonly PrintNotifications printNotices = new();
    private DesktopNotifier? desktopNotifier;
    private void InitializeFeatures()
    {
        desktopNotifier = new DesktopNotifier(!demo && !emulator);
        desktopNotifier.Activated += serial => Dispatcher.UIThread.Post(() =>
        {
            if (Stopping) return;
            selected = entries.FirstOrDefault(e => e.Config.Serial == serial) ?? selected;
            Navigate(selected is null ? "settings" : "printer"); Show(); Activate();
        });
        AddHandler(KeyDownEvent, HandleShortcuts, RoutingStrategies.Tunnel);
    }
    private void ObserveSession(Entry entry)
    {
        var session = entry.Session!;
        printNotices.Reset(entry.Config.Serial);
        session.Changed += () =>
        {
            // Preserve event order if a fast reconnect/report burst finishes before the UI processes it.
            var phase = session.Phase;
            var snapshot = session.Status;
            Dispatcher.UIThread.Post(() =>
            {
                if (Stopping || entry.Session != session || !entries.Contains(entry)) return;
                if (phase != ConnectionPhase.Online) { printNotices.Reset(entry.Config.Serial); return; }
                if (snapshot is not { } status) return;
                foreach (var notification in printNotices.Observe(entry.Config.Serial, status))
                {
                    var allowed = notification.Kind switch { "start" => settings.NotifyStarted, "pause" => settings.NotifyPaused,
                        "end" => notification.TitleKey == "Print finished" ? settings.NotifyFinished : settings.NotifyFailed,
                        _ => settings.NotifyFailed || settings.NotifyPaused };
                    if (!allowed) continue;
                    try { desktopNotifier?.Post(entry.Config.Serial, entry.Config.Name, notification); }
                    catch (Exception ex) { Notify(L("%@: %@", L("Notifications"), ex.Message)); }
                }
            });
        };
    }
    private Control NotificationSettings()
    {
        CheckBox Toggle(string key, bool value, Action<bool> change)
        {
            var check = new CheckBox { Content = L(key), IsChecked = value };
            check.IsCheckedChanged += (_, _) => Guard(() => { change(check.IsChecked == true); Save(); }); return check;
        }
        return Stack(Kicker(L("Notifications")), Card(Stack(
            Toggle("Print finished", settings.NotifyFinished, value => settings.NotifyFinished = value),
            Toggle("Print failed, canceled or an error", settings.NotifyFailed, value => settings.NotifyFailed = value),
            Toggle("Print paused (filament runout, user)", settings.NotifyPaused, value => settings.NotifyPaused = value),
            Toggle("Print started", settings.NotifyStarted, value => settings.NotifyStarted = value))));
    }
    private Control ShortcutSettings()
    {
        var rows = Stack();
        foreach (var (key, shortcut) in new[] { ("Printer", "Ctrl+1"), ("Filament", "Ctrl+2"), ("All Printers", "Ctrl+3"),
            ("Settings", "Ctrl+4 / Ctrl+,"), ("Refresh", "Ctrl+R"), ("Chamber light", "Ctrl+L"), ("Pause", "Ctrl+Shift+P"), ("Stop Print", "Ctrl+Shift+S") })
            rows.Children.Add(Columns("*,Auto", Text(L(key), 12), Text(shortcut, 12, Muted)));
        return Stack(Kicker(L("Controls")), Card(rows));
    }
    private void HandleShortcuts(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Stopping || (e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0) return;
        if (e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4)
        { Navigate(e.Key switch { Key.D1 => "printer", Key.D2 => "filament", Key.D3 => "fleet", _ => "settings" }); e.Handled = true; return; }
        if (e.Key is Key.OemComma) { Navigate("settings"); e.Handled = true; return; }
        if (selected is not { } entry) return;
        switch (e.Key)
        {
            case Key.R: entry.Session?.Refresh(); e.Handled = true; break;
            case Key.L when PrinterModel.Find(entry.Config.ModelCode, entry.Config.Serial)?.Light != false:
                _ = Send(entry, "ledctrl", light: entry.Status?.Light != true); e.Handled = true; break;
            case Key.P when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && entry.Status is { ControlsLocked: false } s && s.Active:
                _ = Send(entry, s.State == JobState.Paused ? "resume" : "pause"); e.Handled = true; break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && entry.Status is { ControlsLocked: false, Active: true }:
                _ = Send(entry, "stop"); e.Handled = true; break;
        }
    }
    private void HandleQuickShortcuts(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Stopping || (e.KeyModifiers & KeyModifiers.Control) == 0 || (e.KeyModifiers & KeyModifiers.Alt) != 0) return;
        if (e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.OemComma)
        { OpenFromQuickPanel(e.Key switch { Key.D1 => "printer", Key.D2 => "filament", Key.D3 => "fleet", _ => "settings" }); e.Handled = true; return; }
        if (selected is not { } entry) return;
        switch (e.Key)
        {
            case Key.R: entry.Session?.Refresh(); e.Handled = true; break;
            case Key.L when PrinterModel.Find(entry.Config.ModelCode, entry.Config.Serial)?.Light != false:
                _ = QuickCommand(entry, "ledctrl", light: entry.Status?.Light != true); e.Handled = true; break;
            case Key.P when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && entry.Status is { ControlsLocked: false, Active: true } s:
                _ = QuickCommand(entry, s.State == JobState.Paused ? "resume" : "pause"); e.Handled = true; break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && entry.Status is { ControlsLocked: false, Active: true }:
                _ = QuickCommand(entry, "stop"); e.Handled = true; break;
        }
    }
    private Control ErrorCard(PrinterIssue issue)
    {
        var severity = issue.Severity switch { IssueSeverity.Fatal => "Error", IssueSeverity.Serious => "Warning", IssueSeverity.Info => "Info", _ => "Notice" };
        var link = ActionButton("arrow", L("Wiki"), () =>
        {
            try { Process.Start(new ProcessStartInfo(issue.Wiki.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Notify(L("%@: %@", L("Error"), ex.Message)); }
        });
        return Card(Stack(Columns("*,Auto", Label("shield", L(severity), issue.Severity <= IssueSeverity.Serious ? Brush("#F27E87") : Muted), link),
            Text(issue.Message, 13), Text(issue.Code, 10, Muted)));
    }
    private Control Telemetry(PrinterStatus s, PrinterModel? model)
    {
        var fans = Stack(Kicker(L("Fans")));
        foreach (var (key, value) in new[] { ("Part", s.PartFan), ("Aux", s.AuxFan), ("Chamber", s.ExhaustFan), ("Hotend", s.HeatbreakFan) })
        {
            var profile = model is null ? null : PrinterCapabilities.Find(model.Code);
            if (key == "Aux" && profile?.AuxFan == false || key == "Chamber" && profile?.ChamberFan == false) continue;
            fans.Children.Add(Columns("*,Auto", Text(L(key), 12, Muted), Text(value is { } percent ? L("%lld%%", percent) : "—", 12)));
        }
        string Nozzle()
        {
            var code = s.NozzleType?.ToUpperInvariant() ?? "";
            var material = code.Length >= 4 ? code.Substring(2, 2) switch { "00" => L("Stainless steel"), "01" => L("Hardened steel"), "05" => L("Tungsten carbide"), _ => s.NozzleType ?? "—" } :
                code.Contains("HARDENED") ? L("Hardened steel") : code.Contains("STAINLESS") ? L("Stainless steel") : s.NozzleType ?? "—";
            if (code.Length >= 2 && code[1] is 'H' or 'E' or 'U') material = L(code[1] == 'U' ? "%@, TPU high flow" : "%@, high flow", material);
            return s.NozzleDiameter is { } diameter ? $"{diameter:0.0} mm · {material}" : material;
        }
        var info = Stack(Kicker(L("Printer")));
        void Detail(string key, string value) => info.Children.Add(Columns("Auto,*", Text(L(key), 11, Muted), Text(value, 11)));
        Detail("Nozzle", Nozzle());
        if (s.SecondNozzle is not null) Detail("In use", L(s.LeftNozzleInUse == true ? "Left nozzle" : "Right nozzle"));
        if (model?.Light != false) Detail("Door", s.DoorOpen is { } door ? L(door ? "Open" : "Closed") : "—");
        Detail("Network", s.WifiSignal is { } signal ? L("%@ · %@", L(signal >= -50 ? "Wi-Fi excellent" : signal >= -60 ? "Wi-Fi good" : signal >= -70 ? "Wi-Fi fair" : signal >= -80 ? "Wi-Fi weak" : "Wi-Fi poor"), $"{signal} dBm") : L("Not measured"));
        Detail("Storage", s.HasSdCard is { } sd ? L(sd ? "SD card inserted" : "No SD card") : "—");
        Detail("Live View", s.LiveViewEnabled is { } live ? L(live ? "On" : "Off") : "—");
        if (s.PlateIndex is { } plate) Detail("Identifying build plate", plate.ToString());
        if (model is not null)
        {
            Detail("Build volume", PrinterCapabilities.Volume(model.Code));
            if (PrinterCapabilities.Find(model.Code) is { } capabilities)
            {
                Detail("Nozzles", L(model.Nozzles > 1 ? "Two, up to %lld °C" : "One, up to %lld °C", capabilities.NozzleMax));
                Detail("Bed", L("Up to %lld °C", capabilities.BedMax));
                Detail("Chamber", capabilities.ChamberMax is { } max ? L("Heated, up to %lld °C", max) : L(model.Chamber ? "Enclosed, with temperature sensor" : model.Light ? "Enclosed" : "Open frame"));
            }
        }
        if (s.FirmwareUpdateAvailable) Detail("Firmware", L("Update available"));
        return Columns("*,*", Card(fans), Card(info));
    }
}
