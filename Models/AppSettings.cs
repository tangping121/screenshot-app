namespace ScreenshotApp.Models;

public enum ResolutionCap
{
    Native,
    P1080,
    P2K
}

public static class ResolutionCaps
{
    public static ResolutionCap Parse(string? value) => value switch
    {
        "1080p" or "P1080" => ResolutionCap.P1080,
        "2K" or "P2K" => ResolutionCap.P2K,
        _ => ResolutionCap.Native
    };

    public static string ToSetting(ResolutionCap cap) => cap switch
    {
        ResolutionCap.P1080 => "1080p",
        ResolutionCap.P2K => "2K",
        _ => "Native"
    };

    public static string Label(ResolutionCap cap) => cap switch
    {
        ResolutionCap.P1080 => "1080p",
        ResolutionCap.P2K => "2K",
        _ => "原尺寸（不超过 2K）"
    };
}

public sealed class AppSettings
{
    public string SaveDirectory { get; set; } = "";
    public string RegionShotHotkey { get; set; } = "Ctrl+Alt+A";
    public string FullShotHotkey { get; set; } = "Ctrl+Alt+S";
    public string RegionRecordHotkey { get; set; } = "Ctrl+Alt+R";
    public string StopRecordHotkey { get; set; } = "Ctrl+Alt+Q";
    public int FrameRate { get; set; } = 30;
    public string ResolutionCap { get; set; } = "Native";
    public bool SystemAudio { get; set; } = true;
    public bool Microphone { get; set; } = false;
    public bool CountdownEnabled { get; set; } = true;

    public AppSettings Clone() => new()
    {
        SaveDirectory = SaveDirectory,
        RegionShotHotkey = RegionShotHotkey,
        FullShotHotkey = FullShotHotkey,
        RegionRecordHotkey = RegionRecordHotkey,
        StopRecordHotkey = StopRecordHotkey,
        FrameRate = FrameRate,
        ResolutionCap = ResolutionCap,
        SystemAudio = SystemAudio,
        Microphone = Microphone,
        CountdownEnabled = CountdownEnabled
    };
}
