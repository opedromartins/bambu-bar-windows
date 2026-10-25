using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using BambuBar.Desktop.Services;

namespace BambuBar.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            desktop.MainWindow = window;
            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://BambuBar/Assets/app.ico"))),
                ToolTipText = "Bambu Bar"
            };
            var menu = new NativeMenu();
            var panel = new NativeMenuItem(window.L("Menu bar"));
            panel.Click += (_, _) => window.ShowQuickPanel();
            var open = new NativeMenuItem(window.L("Open Bambu Bar"));
            open.Click += (_, _) => { window.Show(); window.Activate(); };
            var exit = new NativeMenuItem(window.L("Quit"));
            exit.Click += async (_, _) => { tray.Dispose(); await window.StopAsync(); desktop.Shutdown(); };
            menu.Items.Add(panel); menu.Items.Add(open); menu.Items.Add(new NativeMenuItemSeparator()); menu.Items.Add(exit);
            tray.Menu = menu;
            tray.Clicked += (_, _) => window.ToggleQuickPanel();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            window.TrayTextChanged += text => tray.ToolTipText = text;
            window.TrayIndicatorChanged += state => tray.Icon = TrayProgressIcon.Create(state);
            window.LanguageChanged += () => { panel.Header = window.L("Menu bar"); open.Header = window.L("Open Bambu Bar"); exit.Header = window.L("Quit"); };
            var startInTray = Program.Arguments.Contains("--tray") && !Program.Arguments.Contains("--smoke-test");
            window.Opened += (_, _) => { if (startInTray) { startInTray = false; window.Hide(); } };
            window.Closing += (_, e) => { if (!window.Stopping) { e.Cancel = true; window.Hide(); } };
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (window.Stopping) return;
                e.Cancel = true;
                tray.Dispose();
                await window.StopAsync();
                desktop.Shutdown();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
