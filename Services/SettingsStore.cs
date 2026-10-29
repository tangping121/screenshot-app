using System.Text.Json;
using ScreenshotApp.Models;

namespace ScreenshotApp.Services;

internal static class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string SettingsPath => Path.Combine(Log.DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            settings = new AppSettings();
        }

        if (string.IsNullOrWhiteSpace(settings.SaveDirectory))
        {
            settings.SaveDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "截图录像");
        }

        settings.FrameRate = settings.FrameRate == 60 ? 60 : 30;
        settings.ResolutionCap = ResolutionCaps.ToSetting(ResolutionCaps.Parse(settings.ResolutionCap));
        EnsureFolders(settings);
        return settings;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Log.DirectoryPath);
        EnsureFolders(settings);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static void EnsureFolders(AppSettings settings)
    {
        Directory.CreateDirectory(ScreenshotDirectory(settings));
        Directory.CreateDirectory(RecordingDirectory(settings));
    }

    public static string ScreenshotDirectory(AppSettings settings) => Path.Combine(settings.SaveDirectory, "截图");

    public static string RecordingDirectory(AppSettings settings) => Path.Combine(settings.SaveDirectory, "录像");

    public static string NewFilePath(string directory, string prefix, string extension)
    {
        Directory.CreateDirectory(directory);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(directory, $"{prefix}_{stamp}{extension}");
        int index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{prefix}_{stamp}_{index}{extension}");
            index++;
        }

        return path;
    }
}
