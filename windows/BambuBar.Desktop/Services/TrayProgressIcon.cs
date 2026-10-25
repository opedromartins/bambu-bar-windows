using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BambuBar.Core;

namespace BambuBar.Desktop.Services;

public sealed record TrayVisual(ConnectionPhase Phase, JobState? State, int Progress, bool Canceled = false);

internal static class TrayProgressIcon
{
    private sealed class Glyph(TrayVisual value) : Control
    {
        public override void Render(DrawingContext context)
        {
            var color = Color.Parse(value.Phase != ConnectionPhase.Online || value.Canceled ? "#90A39A" : value.State == JobState.Failed ? "#F48189" : value.State == JobState.Paused ? "#F0BA63" : "#60DE9C");
            var ink = new SolidColorBrush(Color.Parse("#E8F7EF"));
            var accent = new SolidColorBrush(color);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#D41A2520")), null, new Rect(1, 1, 30, 30), 6, 6);
            context.DrawRectangle(null, new Pen(ink, 1.7), new Rect(7, 5, 18, 23), 3, 3);
            var fraction = value.Phase == ConnectionPhase.Online ? Math.Clamp(value.Progress / 100d, 0, 1) : 0;
            context.DrawRectangle(new SolidColorBrush(color, .65), null, new Rect(10, 24 - fraction * 15, 12, fraction * 15), 1, 1);
            context.DrawLine(new Pen(ink, 1.3), new Point(10, 12), new Point(22, 12));
            context.DrawRectangle(ink, null, new Rect(14, 11, 5, 4), 1, 1);
            context.DrawLine(new Pen(ink, 1.3), new Point(10, 25), new Point(22, 25));
            if (value.Phase != ConnectionPhase.Online || !value.Canceled && value.State is JobState.Paused or JobState.Failed)
            {
                context.DrawEllipse(accent, new Pen(new SolidColorBrush(Color.Parse("#1A2520")), 1), new Point(26, 25), 5, 5);
                if (value.State == JobState.Paused && value.Phase == ConnectionPhase.Online)
                { context.DrawLine(new Pen(Brushes.Black, 1.5), new Point(24.5, 22.5), new Point(24.5, 27.5)); context.DrawLine(new Pen(Brushes.Black, 1.5), new Point(27.5, 22.5), new Point(27.5, 27.5)); }
                else { context.DrawLine(new Pen(Brushes.Black, 1.5), new Point(26, 22), new Point(26, 25)); context.DrawEllipse(Brushes.Black, null, new Point(26, 27), .7, .7); }
            }
        }
    }
    public static WindowIcon Create(TrayVisual state)
    {
        var glyph = new Glyph(state) { Width = 32, Height = 32 };
        glyph.Measure(new Size(32, 32)); glyph.Arrange(new Rect(0, 0, 32, 32));
        using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64), new Vector(192, 192));
        bitmap.Render(glyph);
        using var stream = new MemoryStream(); bitmap.Save(stream); stream.Position = 0;
        return new WindowIcon(stream);
    }
}
