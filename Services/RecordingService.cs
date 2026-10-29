using ScreenshotApp.Models;
using ScreenRecorderLib;

namespace ScreenshotApp.Services;

internal sealed class RecordingSession : IDisposable
{
    readonly Recorder _recorder;
    readonly StopwatchClock _clock = new();
    readonly int _fps;
    int _frames;
    long _lastDiskCheck;
    bool _stopRequested;
    bool _nativeStopped;
    bool _disposed;

    public RecordingSession(Recorder recorder, int fps, string outputPath)
    {
        _recorder = recorder;
        _fps = fps;
        OutputPath = outputPath;
        Phase = "录制中";
        _recorder.OnFrameRecorded += OnFrame;
        _recorder.OnRecordingComplete += (_, e) =>
        {
            Phase = "已完成";
            Completed?.Invoke(e.FilePath ?? outputPath);
        };
        _recorder.OnRecordingFailed += (_, e) =>
        {
            Phase = "失败";
            Failed?.Invoke(string.IsNullOrWhiteSpace(e.Error) ? "录像失败。" : e.Error);
        };
    }

    public string OutputPath { get; }
    public string Phase { get; private set; }
    public bool IsPaused { get; private set; }
    public bool FramesDropped { get; private set; }
    public string? Warning { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;

    public event Action<string>? Completed;
    public event Action<string>? Failed;

    public void Start()
    {
        _clock.Start();
        _recorder.Record(OutputPath);
    }

    public void Pause()
    {
        if (IsPaused || _stopRequested)
            return;
        _recorder.Pause();
        _clock.Stop();
        IsPaused = true;
        Phase = "已暂停";
    }

    public void Resume()
    {
        if (!IsPaused || _stopRequested)
            return;
        _recorder.Resume();
        _clock.Start();
        IsPaused = false;
        Phase = FramesDropped ? "录制中 · 出现丢帧" : "录制中";
    }

    public bool IsStopping => _stopRequested;

    public bool TryBeginStop()
    {
        if (_stopRequested)
            return false;
        _stopRequested = true;
        Phase = "正在保存";
        _clock.Stop();
        return true;
    }

    public void StopNative()
    {
        if (_nativeStopped)
            return;
        _nativeStopped = true;
        _recorder.Stop();
    }

    public void Stop()
    {
        TryBeginStop();
        StopNative();
    }

    public void Poll()
    {
        if (!IsPaused && !_stopRequested && _clock.Elapsed.TotalSeconds > 2 && _frames > 0)
        {
            double actual = _frames / _clock.Elapsed.TotalSeconds;
            if (actual < _fps * 0.75)
            {
                FramesDropped = true;
                if (Phase == "录制中")
                    Phase = "录制中 · 出现丢帧";
            }
        }

        long now = Environment.TickCount64;
        if (now - _lastDiskCheck < 10_000)
            return;
        _lastDiskCheck = now;
        try
        {
            string? root = Path.GetPathRoot(OutputPath);
            if (string.IsNullOrEmpty(root))
                return;
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < 100L * 1024 * 1024)
                Warning = "磁盘剩余空间不足，录像可能中断。";
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    void OnFrame(object? sender, FrameRecordedEventArgs e)
    {
        if (!IsPaused)
            Interlocked.Increment(ref _frames);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _recorder.Dispose(); }
        catch (MemberAccessException) { /* 库没有公开 Dispose 时交给终结器 */ }
        catch { /* 录像器已经结束 */ }
    }

    sealed class StopwatchClock
    {
        readonly System.Diagnostics.Stopwatch _watch = new();
        public TimeSpan Elapsed => _watch.Elapsed;
        public void Start() => _watch.Start();
        public void Stop() => _watch.Stop();
    }
}

internal static class RecordingService
{
    public static AudioProbe ProbeAudio()
    {
        try
        {
            var loopback = Recorder.GetSystemAudioLoopbackDevices();
            var microphones = Recorder.GetSystemAudioCaptureDevices();
            bool hasSystem = loopback != null && loopback.Count > 0;
            bool hasMic = microphones != null && microphones.Count > 0;
            return new AudioProbe(
                hasSystem,
                hasMic,
                hasSystem ? "" : "没有可用的播放设备，无法录系统声音",
                hasMic ? "" : "没有可用的麦克风");
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return new AudioProbe(false, false, "无法检测播放设备：" + ex.Message, "无法检测麦克风：" + ex.Message);
        }
    }

    public static RecordingSession Create(MonitorInfo monitor, PixelRect region, RecordChoice choice, string outputPath)
    {
        PixelRect clipped = VideoSizing.ShrinkToEven(region.Intersect(monitor.Bounds));
        if (clipped.Width < 16 || clipped.Height < 16)
            throw new InvalidOperationException("选区太小，无法录像。");

        (int outW, int outH) = VideoSizing.Fit(clipped.Width, clipped.Height, choice.Cap);
        var display = new DisplayRecordingSource(monitor.DeviceName)
        {
            RecorderApi = RecorderApi.WindowsGraphicsCapture,
            IsCursorCaptureEnabled = true,
            IsBorderRequired = false
        };

        int relX = clipped.X - monitor.Bounds.X;
        int relY = clipped.Y - monitor.Bounds.Y;
        bool coversMonitor = relX <= 1 && relY <= 1
            && clipped.Right >= monitor.Bounds.Right - 1
            && clipped.Bottom >= monitor.Bounds.Bottom - 1;
        if (!coversMonitor)
            display.SourceRect = new ScreenRect(Math.Max(0, relX), Math.Max(0, relY), clipped.Width, clipped.Height);

        if (outW != clipped.Width || outH != clipped.Height)
        {
            display.OutputSize = new ScreenSize(outW, outH);
            display.Stretch = StretchMode.Uniform;
        }

        RecorderOptions options = RecorderOptions.Default;
        options.SourceOptions.RecordingSources.Add(display);
        options.OutputOptions.RecorderMode = RecorderMode.Video;
        options.OutputOptions.Stretch = StretchMode.Uniform;
        if (outW != clipped.Width || outH != clipped.Height)
            options.OutputOptions.OutputFrameSize = new ScreenSize(outW, outH);

        options.VideoEncoderOptions.Framerate = choice.FrameRate == 60 ? 60 : 30;
        options.VideoEncoderOptions.Bitrate = VideoSizing.Bitrate(outW, outH, options.VideoEncoderOptions.Framerate);
        options.VideoEncoderOptions.IsHardwareEncodingEnabled = true;
        options.VideoEncoderOptions.IsFixedFramerate = false;
        // 结束后把文件头挪到开头会把整段视频再复制一遍，2K 录像会卡很久。本地播放不需要这一步。
        options.VideoEncoderOptions.IsMp4FastStartEnabled = false;
        options.VideoEncoderOptions.Encoder = new H264VideoEncoder
        {
            BitrateMode = H264BitrateControlMode.UnconstrainedVBR,
            EncoderProfile = H264Profile.High
        };

        var audio = new List<AudioSourceBase>();
        if (choice.SystemAudio)
        {
            LoopbackAudioSource? loop = LoopbackAudioSource.Default;
            if (loop == null)
                throw new InvalidOperationException("没有可用的播放设备，无法录系统声音。");
            audio.Add(loop);
        }

        if (choice.Microphone)
        {
            CaptureAudioSource? microphone = CaptureAudioSource.Default;
            if (microphone == null)
                throw new InvalidOperationException("没有可用的麦克风。");
            audio.Add(microphone);
        }

        options.AudioOptions.IsAudioEnabled = audio.Count > 0;
        options.AudioOptions.AudioSources = audio;
        options.AudioOptions.Channels = AudioChannels.Stereo;
        options.AudioOptions.Bitrate = AudioBitrate.bitrate_128kbps;
        options.MouseOptions.IsMousePointerEnabled = false;
        options.MouseOptions.IsMouseClicksDetected = false;
        options.LogOptions.IsLogEnabled = true;
        options.LogOptions.LogFilePath = Path.Combine(Log.DirectoryPath, "recorder.log");
        options.LogOptions.LogSeverityLevel = LogLevel.Info;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Log.Info($"开始录像 {outW}x{outH} @{options.VideoEncoderOptions.Framerate} 码率 {options.VideoEncoderOptions.Bitrate} 声音 {audio.Count} 路 -> {outputPath}");
        Recorder recorder = Recorder.CreateRecorder(options);
        return new RecordingSession(recorder, options.VideoEncoderOptions.Framerate, outputPath);
    }
}

internal readonly record struct AudioProbe(bool HasSystem, bool HasMicrophone, string SystemReason, string MicrophoneReason);
