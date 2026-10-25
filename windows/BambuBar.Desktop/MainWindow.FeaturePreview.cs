using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using BambuBar.Core;
using BambuBar.Desktop.Controls;

namespace BambuBar.Desktop;

public sealed partial class MainWindow
{
    private async Task PreviewFeatures()
    {
        if (!demo) throw new InvalidOperationException("Feature UI checks require demo mode.");
        var entry = entries.First(); var original = entry.Demo!;
        selected = entry; page = "printer"; BuildShell(); await Task.Delay(100);
        var view = interactiveViews.First(p => p.Key.StartsWith(entry.Config.Serial + "/", StringComparison.Ordinal) && p.Value.Height == 258).Value;
        if (view.TriangleCount < 1000) throw new InvalidOperationException("3D geometry missing.");
        var initialCamera = view.Camera;
        view.Orbit(.6, .15); view.Zoom(1.1);
        RenderPage();
        if (view.Camera == initialCamera || !interactiveViews.Values.Contains(view)) throw new InvalidOperationException("3D orbit reset during report refresh.");
        await Task.Delay(120);
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96)))
        { bitmap.Render(this); bitmap.Save("build/windows/preview-3d-orbit.png"); }
        view.Update(original with { Progress = 20 }, true); var low = view.VisibleParts;
        view.Update(original with { Progress = 80 }, true);
        if (view.VisibleParts <= low) throw new InvalidOperationException("3D print slices do not grow with progress.");
        view.ResetView(); view.Update(original, true);
        var gallery = new Window { Width = 1120, Height = 340, Background = Brush("#1B2520"), ShowInTaskbar = false, FontFamily = FontFamily };
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(10) };
        foreach (var (shape, name) in new[] { ("enclosed", "P2S"), ("wide", "H2D"), ("openFrame", "P1P"), ("bedSlinger", "A1"), ("mini", "A1 mini") })
        {
            var model = new Printer3DView(shape) { Width = 200, Height = 270 };
            model.Update(original with { State = JobState.Paused }, true);
            if (model.TriangleCount < 1000) throw new InvalidOperationException("3D family geometry missing.");
            grid.Children.Add(Stack(Text(name, 16), model));
        }
        gallery.Content = grid; gallery.Show(); await Task.Delay(120);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(1120, 340), new Vector(96, 96)))
        { bitmap.Render(gallery); bitmap.Save("build/windows/preview-3d-families.png"); }
        gallery.Close();
        void Key(Key key, KeyModifiers modifiers = KeyModifiers.Control)
        {
            var args = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers };
            RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("App shortcut not handled.");
        }
        try
        {
            Key(Avalonia.Input.Key.D2); if (page != "filament") throw new InvalidOperationException("Navigation shortcut failed.");
            Key(Avalonia.Input.Key.D1); Key(Avalonia.Input.Key.R);
            Key(Avalonia.Input.Key.L); if (entry.Demo?.Light == original.Light) throw new InvalidOperationException("Light shortcut failed.");
            Key(Avalonia.Input.Key.P, KeyModifiers.Control | KeyModifiers.Shift);
            if (entry.Demo?.State != JobState.Paused) throw new InvalidOperationException("Pause shortcut failed.");
            Key(Avalonia.Input.Key.P, KeyModifiers.Control | KeyModifiers.Shift);
            if (entry.Demo?.State != JobState.Printing) throw new InvalidOperationException("Resume shortcut failed.");
            Key(Avalonia.Input.Key.OemComma); if (page != "settings") throw new InvalidOperationException("Settings shortcut failed.");
            entry.Demo = original with { State = JobState.Paused, StageId = 6, Issues = [new PrinterIssue("HMS_0700_2000_0002_0001", L("Filament ran out")) { Severity = IssueSeverity.Serious }] };
            page = "printer"; BuildShell();
            if (detail.Parent is ScrollViewer scroll) scroll.Offset = new Vector(0, 390);
            await Task.Delay(150);
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96)))
            { bitmap.Render(this); bitmap.Save("build/windows/preview-telemetry-errors.png"); }
            Console.WriteLine("Features UI: five real 3D families, orbit/zoom persistence, growing slices, shortcuts and telemetry/errors passed.");
        }
        finally { entry.Demo = original; page = "printer"; BuildShell(); }
    }
}
