using System.Windows.Interop;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class PinWindow : Window
{
    public PinWindow(ImageSource image)
    {
        InitializeComponent();
        Photo.Source = image;
        int pixelW = image is BitmapSource bitmap ? bitmap.PixelWidth : 400;
        int pixelH = image is BitmapSource bitmap2 ? bitmap2.PixelHeight : 300;
        double maxW = SystemParameters.WorkArea.Width * 0.8;
        double maxH = SystemParameters.WorkArea.Height * 0.8;
        double scale = Math.Min(1, Math.Min(maxW / pixelW, maxH / pixelH));
        Width = Math.Max(80, pixelW * scale);
        Height = Math.Max(60, pixelH * scale);
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.MakeToolWindow(hwnd);
        };
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.OriginalSource is Button)
            return;
        DragMove();
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
