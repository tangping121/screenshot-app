using System.Windows.Interop;
using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

internal partial class RecordingBarWindow : Window
{
    readonly RecordingSession _session;
    readonly MonitorInfo _monitor;
    readonly DispatcherTimer _timer;
    bool _saving;
    int _dots;

    public RecordingBarWindow(RecordingSession session, MonitorInfo monitor)
    {
        _session = session;
        _monitor = monitor;
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        SourceInitialized += (_, _) => Place();
    }

    public event Action? PauseClicked;
    public event Action? StopClicked;

    public void ShowSaving()
    {
        _saving = true;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        TimeText.Text = "保存中";
        HintText.Text = "正在写入文件，请稍候";
    }

    public void ForceClose()
    {
        _saving = true;
        Close();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.OriginalSource is Button)
            return;
        DragMove();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_saving && _session.Phase is not ("已完成" or "失败"))
        {
            e.Cancel = true;
            StopClicked?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    void Place()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeToolWindow(hwnd);
        NativeMethods.ExcludeFromCapture(hwnd);
        UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        double scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        double scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        double width = ActualWidth * scaleX;
        double height = ActualHeight * scaleY;
        int x = _monitor.Bounds.X + (int)((_monitor.Bounds.Width - width) / 2);
        int y = _monitor.Bounds.Bottom - (int)height - (int)(28 * scaleY);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, (int)Math.Ceiling(width), (int)Math.Ceiling(height), NativeMethods.SwpShowWindow);
    }

    void Refresh()
    {
        if (_saving)
        {
            _dots = (_dots + 1) % 4;
            HintText.Text = "正在写入文件，请稍候" + new string('·', _dots);
            return;
        }
        _session.Poll();
        TimeSpan elapsed = _session.Elapsed;
        TimeText.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        PauseButton.Content = _session.IsPaused ? "继续" : "暂停";
        HintText.Text = _session.Warning ?? (_session.FramesDropped ? "画面跟不上，已丢帧" : _session.Phase);
    }

    void Pause_Click(object sender, RoutedEventArgs e) => PauseClicked?.Invoke();
    void Stop_Click(object sender, RoutedEventArgs e) => StopClicked?.Invoke();
}
