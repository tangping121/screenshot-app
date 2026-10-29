using System.Windows.Interop;
using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class CountdownWindow : Window
{
    readonly MonitorInfo _monitor;

    public CountdownWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        InitializeComponent();
        SourceInitialized += (_, _) => Place();
    }

    public static async Task PlayAsync(MonitorInfo monitor)
    {
        var window = new CountdownWindow(monitor);
        window.Show();
        try
        {
            for (int i = 3; i >= 1; i--)
            {
                window.NumberText.Text = i.ToString();
                await Task.Delay(1000);
            }
        }
        finally
        {
            window.Close();
        }
    }

    void Place()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeToolWindow(hwnd, clickThrough: true);
        NativeMethods.ExcludeFromCapture(hwnd);
        var source = PresentationSource.FromVisual(this);
        double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        double width = Width * scaleX;
        double height = Height * scaleY;
        int x = _monitor.Bounds.X + (int)((_monitor.Bounds.Width - width) / 2);
        int y = _monitor.Bounds.Y + (int)((_monitor.Bounds.Height - height) / 2);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, (int)width, (int)height, NativeMethods.SwpShowWindow);
    }
}
