using System.Runtime.InteropServices;

namespace ScreenshotApp.Services;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const uint SwpShowWindow = 0x0040;

    public static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(IntPtr hWnd, int index, nint value);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const int GwlExStyle = -20;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTransparent = 0x00000020;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    public static void MakeToolWindow(IntPtr hwnd, bool clickThrough = false)
    {
        nint style = GetWindowLongPtr(hwnd, GwlExStyle);
        style |= WsExToolWindow;
        if (clickThrough)
            style |= WsExTransparent;
        SetWindowLongPtr(hwnd, GwlExStyle, style);
    }

    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;
        SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        try
        {
            ScreenRecorderLib.Recorder.SetExcludeFromCapture(hwnd, true);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}
