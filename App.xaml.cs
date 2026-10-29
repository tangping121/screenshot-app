using System.Threading;
using ScreenshotApp.Services;

namespace ScreenshotApp;

public partial class App : Application
{
    Mutex? _mutex;
    CaptureController? _controller;
    bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception);
            MessageBox.Show(args.Exception.Message, "截图录像", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _mutex = new Mutex(true, @"Local\XiaoXue.ScreenshotRecorder", out bool created);
            if (!created)
            {
                MessageBox.Show("截图录像已经在运行。请点击托盘图标打开。", "截图录像");
                Shutdown();
                return;
            }

            _ownsMutex = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        _controller = new CaptureController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
