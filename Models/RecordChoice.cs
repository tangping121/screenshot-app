namespace ScreenshotApp.Models;

public sealed class RecordChoice
{
    public ResolutionCap Cap { get; init; } = ResolutionCap.Native;
    public int FrameRate { get; init; } = 30;
    public bool SystemAudio { get; init; }
    public bool Microphone { get; init; }

    public static RecordChoice FromSettings(AppSettings settings) => new()
    {
        Cap = ResolutionCaps.Parse(settings.ResolutionCap),
        FrameRate = settings.FrameRate == 60 ? 60 : 30,
        SystemAudio = settings.SystemAudio,
        Microphone = settings.Microphone
    };
}

public sealed class OverlayPick
{
    public required PixelRect Region { get; init; }
    public RecordChoice? Recording { get; init; }
}

public sealed class HistoryEntry
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string TimeText { get; init; }
    public required string SizeText { get; init; }
    public required DateTime Time { get; init; }
    public bool IsImage { get; init; }
}

public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required PixelRect Bounds { get; init; }
}
