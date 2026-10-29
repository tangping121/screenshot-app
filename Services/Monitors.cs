using ScreenshotApp.Models;

namespace ScreenshotApp.Services;

internal static class Monitors
{
    public static MonitorInfo UnderCursor()
    {
        NativeMethods.GetCursorPos(out NativeMethods.NativePoint point);
        return FromPoint(point.X, point.Y);
    }

    public static MonitorInfo FromPoint(int x, int y)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y));
        return FromScreen(screen);
    }

    public static MonitorInfo Primary()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen
            ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault()
            ?? throw new InvalidOperationException("没有找到显示器。");
        return FromScreen(screen);
    }

    static MonitorInfo FromScreen(System.Windows.Forms.Screen screen)
    {
        var bounds = screen.Bounds;
        return new MonitorInfo
        {
            DeviceName = screen.DeviceName,
            Bounds = new PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)
        };
    }
}
