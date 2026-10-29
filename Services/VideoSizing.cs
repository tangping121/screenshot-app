using ScreenshotApp.Models;

namespace ScreenshotApp.Services;

internal static class VideoSizing
{
    public const int MaxWidth = 2560;
    public const int MaxHeight = 1440;

    public static (int Width, int Height) Fit(int srcWidth, int srcHeight, ResolutionCap cap)
    {
        int maxW = cap == ResolutionCap.P1080 ? 1920 : MaxWidth;
        int maxH = cap == ResolutionCap.P1080 ? 1080 : MaxHeight;
        if (srcWidth < 2 || srcHeight < 2)
            return (2, 2);

        double scale = Math.Min(1d, Math.Min(maxW / (double)srcWidth, maxH / (double)srcHeight));
        int width = Even(Math.Max(2, (int)Math.Round(srcWidth * scale)));
        int height = Even(Math.Max(2, (int)Math.Round(srcHeight * scale)));
        if (width > maxW)
            width = Even(maxW);
        if (height > maxH)
            height = Even(maxH);
        return (Math.Max(2, width), Math.Max(2, height));
    }

    public static int Bitrate(int width, int height, int fps)
    {
        bool is2K = width > 1920 || height > 1080;
        int tier = (is2K, fps >= 60) switch
        {
            (false, false) => 8_000_000,
            (false, true) => 12_000_000,
            (true, false) => 16_000_000,
            (true, true) => 24_000_000
        };
        double reference = is2K ? 2560d * 1440d : 1920d * 1080d;
        double factor = Math.Clamp((width * (double)height) / reference, 0.35, 1);
        return (int)Math.Round(tier * factor);
    }

    public static PixelRect ShrinkToEven(PixelRect rect)
    {
        int width = rect.Width - (rect.Width % 2);
        int height = rect.Height - (rect.Height % 2);
        return new PixelRect(rect.X, rect.Y, Math.Max(2, width), Math.Max(2, height));
    }

    public static void SelfCheck()
    {
        Expect(Fit(3840, 2160, ResolutionCap.Native), (2560, 1440), "4K 原尺寸应缩到 2K");
        Expect(Fit(3840, 2160, ResolutionCap.P2K), (2560, 1440), "4K 限制 2K");
        Expect(Fit(3840, 2160, ResolutionCap.P1080), (1920, 1080), "4K 限制 1080p");
        Expect(Fit(1920, 1080, ResolutionCap.Native), (1920, 1080), "1080p 保持原尺寸");
        Expect(Fit(1280, 720, ResolutionCap.P2K), (1280, 720), "小于 2K 不放大");
        Expect(Fit(2560, 1440, ResolutionCap.P1080), (1920, 1080), "2K 画面限制到 1080p");
        if (Bitrate(1920, 1080, 30) != 8_000_000)
            throw new InvalidOperationException("1080p30 码率应为 8 Mbps");
        if (Bitrate(1920, 1080, 60) != 12_000_000)
            throw new InvalidOperationException("1080p60 码率应为 12 Mbps");
        if (Bitrate(2560, 1440, 30) != 16_000_000)
            throw new InvalidOperationException("2K30 码率应为 16 Mbps");
        if (Bitrate(2560, 1440, 60) != 24_000_000)
            throw new InvalidOperationException("2K60 码率应为 24 Mbps");
    }

    static void Expect((int Width, int Height) actual, (int Width, int Height) expected, string message)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{message}，实际 {actual.Width}x{actual.Height}");
    }

    static int Even(int value) => value - (value % 2);
}
