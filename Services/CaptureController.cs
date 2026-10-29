using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows.Interop;
using ScreenshotApp.Models;
using ScreenshotApp.Views;

namespace ScreenshotApp.Services;

internal sealed class CaptureController
{
    MainWindow? _main;
    HotkeyHost? _hotkeys;
    System.Windows.Forms.NotifyIcon? _tray;
    System.Drawing.Icon? _trayIcon;
    RecordingSession? _session;
    RecordingBarWindow? _bar;
    bool _mainWasVisible;
    bool _captureBusy;
    bool _exitRequested;
    bool _recordingFinished;

    public AppSettings Settings { get; private set; } = new();
    public bool IsRecording => _session != null;
    public bool IsBusy => _captureBusy || IsRecording;
    public event Action? StateChanged;

    public void Start()
    {
        VideoSizing.SelfCheck();
        Settings = SettingsStore.Load();
        _main = new MainWindow(this);
        _main.SourceInitialized += (_, _) =>
        {
            _hotkeys = new HotkeyHost(_main, this);
            _hotkeys.Attach();
            string? error = _hotkeys.Apply(Settings);
            if (error != null)
                Notify(error);
        };
        CreateTray();
        _main.Show();
        ReloadHistory();
    }

    public void StartRegionScreenshot() => _ = RunRegionScreenshotAsync();

    public void StartFullScreenshot() => _ = RunFullScreenshotAsync();

    public void StartRegionRecording() => _ = RunRegionRecordingAsync();

    public void StartFullRecording() => _ = RunFullRecordingAsync();

    public void StopRecording()
    {
        RecordingSession? session = _session;
        if (session == null || !session.TryBeginStop())
            return;
        _bar?.ShowSaving();
        if (_tray != null)
            _tray.Text = "正在保存录像…";
        Notify("正在保存录像，请稍候。");
        _ = FinishStopAsync(session);
    }

    async Task FinishStopAsync(RecordingSession session)
    {
        var done = new TaskCompletionSource<(string? File, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(string file) => done.TrySetResult((file, null));
        void OnFailed(string error) => done.TrySetResult((null, error));
        session.Completed += OnCompleted;
        session.Failed += OnFailed;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Run(session.StopNative);
            Task finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(2)));
            if (finished != done.Task)
            {
                FinishRecording(File.Exists(session.OutputPath) ? session.OutputPath : null, File.Exists(session.OutputPath) ? null : "保存超时。");
                return;
            }

            (string? file, string? error) = await done.Task;
            FinishRecording(file, error);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            FinishRecording(null, ex.Message);
        }
        finally
        {
            session.Completed -= OnCompleted;
            session.Failed -= OnFailed;
        }
    }

    public void TogglePause()
    {
        if (_session == null)
            return;
        try
        {
            if (_session.IsPaused)
                _session.Resume();
            else
                _session.Pause();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify(ex.Message);
        }
    }

    public string? TryApplySettings(AppSettings next)
    {
        SettingsStore.EnsureFolders(next);
        string? error = _hotkeys?.Apply(next);
        if (error != null)
        {
            _hotkeys?.Apply(Settings);
            return error;
        }

        Settings = next.Clone();
        SettingsStore.Save(Settings);
        _main?.Reload();
        return null;
    }

    public IReadOnlyList<HistoryEntry> LoadHistory()
    {
        var files = new List<HistoryEntry>();
        AddHistory(files, SettingsStore.ScreenshotDirectory(Settings), "截图", true);
        AddHistory(files, SettingsStore.RecordingDirectory(Settings), "录像", false);
        return files.OrderByDescending(item => item.Time).Take(80).ToList();
    }

    public void ReloadHistory() => _main?.Reload();

    public void OpenSaveFolder()
    {
        SettingsStore.EnsureFolders(Settings);
        Process.Start(new ProcessStartInfo(Settings.SaveDirectory) { UseShellExecute = true });
    }

    public void OpenEntry(HistoryEntry entry)
    {
        Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
    }

    public void RevealEntry(HistoryEntry entry)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.FilePath}\"") { UseShellExecute = true });
    }

    public void CopyEntry(HistoryEntry entry)
    {
        if (entry.IsImage)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(entry.FilePath);
            image.EndInit();
            image.Freeze();
            CopyImage(image);
            return;
        }

        var files = new StringCollection { entry.FilePath };
        Clipboard.SetFileDropList(files);
        Notify("已复制文件。");
    }

    public void DeleteEntry(HistoryEntry entry)
    {
        if (MessageBox.Show($"删除 {entry.Name}？", "截图录像", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        File.Delete(entry.FilePath);
        ReloadHistory();
    }

    public string SaveImage(BitmapSource image)
    {
        string path = SettingsStore.NewFilePath(SettingsStore.ScreenshotDirectory(Settings), "Screenshot", ".png");
        WritePng(image, path);
        ReloadHistory();
        try
        {
            CopyImage(image, "截图已保存，并已复制到剪贴板，可以直接粘贴。");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            Notify("截图已保存。剪贴板被占用，请再点一次「复制」。");
        }
        return path;
    }

    public void CopyImage(BitmapSource image) => CopyImage(image, "截图已复制，可以直接粘贴。");

    void CopyImage(BitmapSource image, string message)
    {
        ClipboardImages.Set(image);
        Notify(message);
    }

    public void PinImage(BitmapSource image)
    {
        if (image.CanFreeze && !image.IsFrozen)
            image.Freeze();
        var pin = new PinWindow(image);
        pin.Show();
    }

    public void Notify(string text)
    {
        try
        {
            _tray?.ShowBalloonTip(2500, "截图录像", text, System.Windows.Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    public void RequestExit()
    {
        if (IsRecording)
        {
            if (MessageBox.Show("正在录像，退出会停止并保存。继续退出？", "截图录像", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try { _session?.Stop(); } catch (Exception ex) { Log.Error(ex); }
        }

        _exitRequested = true;
        _hotkeys?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
        if (_main != null)
        {
            _main.AllowClose = true;
            _main.Close();
        }
        Application.Current.Shutdown();
    }

    public bool ExitRequested => _exitRequested;

    async Task RunRegionScreenshotAsync()
    {
        if (!BeginCapture())
            return;
        try
        {
            MonitorInfo monitor = Monitors.UnderCursor();
            var overlay = new RegionOverlayWindow(monitor, recording: false, Settings);
            OverlayPick? pick = await overlay.PickAsync();
            if (pick == null)
                return;
            await WaitForCompositorAsync();
            BitmapSource bitmap = await ScreenshotService.CaptureAsync(monitor, pick.Region);
            ShowAnnotation(bitmap);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowError("截图失败", ex.Message);
        }
        finally
        {
            EndCapture();
        }
    }

    async Task RunFullScreenshotAsync()
    {
        if (!BeginCapture())
            return;
        bool restoreMain = _main?.IsVisible == true;
        try
        {
            MonitorInfo monitor = Monitors.UnderCursor();
            if (restoreMain)
                _main?.Hide();
            await WaitForCompositorAsync();
            BitmapSource bitmap = await ScreenshotService.CaptureAsync(monitor, monitor.Bounds);
            ShowAnnotation(bitmap);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowError("截图失败", ex.Message);
        }
        finally
        {
            if (restoreMain)
                _main?.Show();
            EndCapture();
        }
    }

    async Task RunRegionRecordingAsync()
    {
        if (!BeginCapture())
            return;
        OverlayPick? pick;
        MonitorInfo monitor;
        try
        {
            monitor = Monitors.UnderCursor();
            var overlay = new RegionOverlayWindow(monitor, recording: true, Settings);
            pick = await overlay.PickAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            ShowError("录像", ex.Message);
            EndCapture();
            return;
        }

        EndCapture();
        if (pick?.Recording == null)
            return;
        await BeginRecordingAsync(monitor, pick.Region, pick.Recording);
    }

    async Task RunFullRecordingAsync()
    {
        if (!BeginCapture())
            return;
        EndCapture();
        MonitorInfo monitor = _main == null ? Monitors.UnderCursor() : MonitorOf(_main);
        RecordChoice? choice = RecordOptionsWindow.Ask(_main, Settings);
        if (choice == null)
            return;
        await BeginRecordingAsync(monitor, monitor.Bounds, choice);
    }

    async Task BeginRecordingAsync(MonitorInfo monitor, PixelRect region, RecordChoice choice)
    {
        if (IsRecording || _captureBusy)
        {
            Notify("正在处理上一项操作。");
            return;
        }

        string path = SettingsStore.NewFilePath(SettingsStore.RecordingDirectory(Settings), "Recording", ".mp4");
        if (!ConfirmDiskSpace(path))
            return;

        _recordingFinished = false;
        _mainWasVisible = _main?.IsVisible == true;
        _main?.Hide();
        RaiseState();
        try
        {
            if (Settings.CountdownEnabled)
                await CountdownWindow.PlayAsync(monitor);

            RecordingSession session = RecordingService.Create(monitor, region, choice, path);
            _session = session;
            session.Failed += error => RunOnUi(() =>
            {
                if (!session.IsStopping)
                    FinishRecording(null, error);
            });

            _bar = new RecordingBarWindow(session, monitor);
            _bar.PauseClicked += TogglePause;
            _bar.StopClicked += StopRecording;
            _bar.Show();
            IntPtr hwnd = new WindowInteropHelper(_bar).Handle;
            NativeMethods.ExcludeFromCapture(hwnd);
            session.Start();
            RaiseState();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            FinishRecording(null, DescribeRecorderError(ex));
        }
    }

    void FinishRecording(string? file, string? error)
    {
        if (_recordingFinished)
            return;
        _recordingFinished = true;
        RecordingBarWindow? bar = _bar;
        _bar = null;
        RecordingSession? session = _session;
        _session = null;
        try { bar?.ForceClose(); } catch { /* 窗口可能已经关闭 */ }
        try { session?.Dispose(); } catch { /* 同上 */ }

        if (_mainWasVisible)
            _main?.Show();
        ReloadHistory();
        RaiseState();

        if (!string.IsNullOrWhiteSpace(error))
        {
            Log.Info(error);
            ShowError("录像失败", error);
            return;
        }

        if (_tray != null)
            _tray.Text = "截图录像";
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            ShowMain();
            MessageBox.Show(_main, $"录像已保存。\n{file}", "截图录像", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowError("录像失败", "没有生成录像文件。");
    }

    static void RunOnUi(Action action)
    {
        Dispatcher dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    static void ShowError(string title, string message)
    {
        void Show() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        RunOnUi(Show);
    }

    bool BeginCapture()
    {
        if (IsRecording)
        {
            Notify("正在录像，请先停止。");
            return false;
        }

        if (_captureBusy)
            return false;
        _captureBusy = true;
        RaiseState();
        return true;
    }

    void EndCapture()
    {
        _captureBusy = false;
        RaiseState();
    }

    void ShowAnnotation(BitmapSource bitmap)
    {
        void Open()
        {
            var window = new AnnotationWindow(bitmap, this);
            window.Show();
        }

        if (Application.Current.Dispatcher.CheckAccess())
            Open();
        else
            Application.Current.Dispatcher.Invoke(Open);
    }

    static async Task WaitForCompositorAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(160);
    }

    static MonitorInfo MonitorOf(Window window)
    {
        var helper = new WindowInteropHelper(window);
        NativeMethods.GetCursorPos(out NativeMethods.NativePoint point);
        if (helper.Handle != IntPtr.Zero)
            return Monitors.FromPoint(point.X, point.Y);
        return Monitors.UnderCursor();
    }

    static bool ConfirmDiskSpace(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return true;
            long free = new DriveInfo(root).AvailableFreeSpace;
            if (free < 100L * 1024 * 1024)
            {
                MessageBox.Show("磁盘剩余空间不足 100 MB，无法开始录像。", "截图录像", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (free < 512L * 1024 * 1024)
            {
                return MessageBox.Show("磁盘剩余空间不足 512 MB，录像可能很快写满。仍要开始吗？", "截图录像", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }

        return true;
    }

    static string DescribeRecorderError(Exception ex)
    {
        string detail = ex.ToString();
        if (ex is DllNotFoundException or BadImageFormatException or TypeInitializationException
            || detail.Contains("vcruntime", StringComparison.OrdinalIgnoreCase))
            return "无法加载录像组件。请安装 Microsoft Visual C++ 2015–2022（x64）运行库后重试。";
        return ex.Message;
    }

    static void WritePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image.Clone()));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    static void AddHistory(List<HistoryEntry> files, string directory, string kind, bool image)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            var info = new FileInfo(file);
            files.Add(new HistoryEntry
            {
                FilePath = file,
                Name = info.Name,
                Kind = kind,
                Time = info.LastWriteTime,
                TimeText = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                SizeText = FormatSize(info.Length),
                IsImage = image
            });
        }
    }

    static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024d).ToString("0.0") + " KB";
        return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
    }

    void CreateTray()
    {
        _trayIcon = CreateIcon();
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "截图录像"
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => ShowMain());
        menu.Items.Add("区域截图", null, (_, _) => StartRegionScreenshot());
        menu.Items.Add("全屏截图", null, (_, _) => StartFullScreenshot());
        menu.Items.Add("区域录像", null, (_, _) => StartRegionRecording());
        menu.Items.Add("全屏录像", null, (_, _) => StartFullRecording());
        menu.Items.Add("停止录像", null, (_, _) => StopRecording());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => RequestExit());
        // WPF 里托盘的默认左右键经常收不到，所以在 MouseUp 里自己处理。
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                ShowMain();
                return;
            }

            if (e.Button != System.Windows.Forms.MouseButtons.Right)
                return;
            IntPtr hwnd = _main == null ? IntPtr.Zero : new WindowInteropHelper(_main).Handle;
            if (hwnd != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(hwnd);
            menu.Show(System.Windows.Forms.Cursor.Position);
        };
    }

    static System.Drawing.Icon CreateIcon()
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(System.Drawing.Color.Transparent);
            using var background = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 37, 99, 235));
            graphics.FillEllipse(background, 0, 0, 31, 31);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
            graphics.DrawRectangle(pen, 8, 10, 16, 11);
            using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            graphics.FillRectangle(white, 13, 7, 6, 4);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    void ShowMain()
    {
        if (_main == null)
            return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
        _main.Reload();
    }

    void RaiseState() => StateChanged?.Invoke();
}
