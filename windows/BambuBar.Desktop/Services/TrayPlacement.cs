using System.Runtime.InteropServices;
using Avalonia;

namespace BambuBar.Desktop.Services;

internal static class TrayPlacement
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    public static PixelPoint? Cursor()
    {
        if (OperatingSystem.IsWindows() && GetCursorPos(out var point)) return new(point.X, point.Y);
        return null;
    }
    // Both the anchor and working area are physical pixels, including on mixed-DPI monitors.
    public static PixelPoint Place(PixelPoint anchor, PixelRect area, PixelSize size, int gap)
    {
        var x = anchor.X - size.Width + gap;
        var y = anchor.Y - size.Height - gap;
        if (y < area.Y + gap) y = anchor.Y + gap;
        return new(Math.Clamp(x, area.X + gap, Math.Max(area.X + gap, area.Right - size.Width - gap)),
            Math.Clamp(y, area.Y + gap, Math.Max(area.Y + gap, area.Bottom - size.Height - gap)));
    }
}
