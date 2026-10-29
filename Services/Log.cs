namespace ScreenshotApp.Services;

internal static class Log
{
    static readonly object Gate = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "截图录像");

    public static string FilePath => Path.Combine(DirectoryPath, "app.log");

    public static void Info(string message) => Write(message);

    public static void Error(Exception exception) => Write(exception.ToString());

    static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响截图和录像。
        }
    }
}
