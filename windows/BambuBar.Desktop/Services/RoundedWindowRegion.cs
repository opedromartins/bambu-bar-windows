using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace BambuBar.Desktop.Services;

// Border.CornerRadius only clips Avalonia content. The HWND/backdrop must have the same outline.
// SetWindowRgn owns a successfully assigned HRGN; we only delete regions that were not transferred.
// https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn
internal static class RoundedWindowRegion
{
    public const double PanelRadius = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(nint region, int x, int y);

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var applying = false;
        (int Width, int Height, int Diameter)? previous = null;
        void Apply(bool force = false)
        {
            if (applying || window.TryGetPlatformHandle() is not { Handle: not 0, HandleDescriptor: "HWND" } platform ||
                !GetWindowRect(platform.Handle, out var rect)) return;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return;
            var diameter = Math.Min(Math.Min(width, height), (int)Math.Ceiling(PanelRadius * 2 * window.RenderScaling));
            var geometry = (width, height, diameter);
            if (!force && previous == geometry) return;
            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
            if (region == 0) { Debug.WriteLine("Bambu Bar: could not create the panel window region."); return; }
            applying = true;
            try
            {
                if (SetWindowRgn(platform.Handle, region, true) != 0)
                {
                    region = 0; // Ownership transferred to Windows. Do not free this handle.
                    previous = geometry;
                }
                else Debug.WriteLine("Bambu Bar: could not apply the panel window region.");
            }
            finally
            {
                if (region != 0) DeleteObject(region);
                applying = false;
            }
        }
        window.Opened += (_, _) =>
        {
            Apply(true);
            // Reapply after the compositor finishes setting up the borderless window/backdrop.
            Dispatcher.UIThread.Post(() => { if (window.IsVisible) Apply(true); }, DispatcherPriority.Loaded);
        };
        window.Resized += (_, _) => Apply();
        window.ScalingChanged += (_, _) => Apply();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Window.ActualTransparencyLevel) && window.IsVisible) Apply(true);
        };
    }

    // Runs in the Windows UI smoke check. Rendering a PNG cannot detect an HWND's black corners.
    public static void Verify(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var platform = window.TryGetPlatformHandle() ?? throw new InvalidOperationException("Missing panel HWND.");
        if (!GetWindowRect(platform.Handle, out var rect)) throw new InvalidOperationException("Could not inspect panel bounds.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var region = CreateRoundRectRgn(0, 0, 1, 1, 0, 0);
        if (region == 0) throw new InvalidOperationException("Could not allocate region for verification.");
        try
        {
            if (GetWindowRgn(platform.Handle, region) <= 0 ||
                PtInRegion(region, 0, 0) || PtInRegion(region, width - 1, 0) ||
                PtInRegion(region, 0, height - 1) || PtInRegion(region, width - 1, height - 1) ||
                !PtInRegion(region, width / 2, height / 2) || !PtInRegion(region, width / 2, 1))
                throw new InvalidOperationException("Panel HWND must exclude all four corners and retain its center and edges.");
        }
        finally { DeleteObject(region); }
    }
}
