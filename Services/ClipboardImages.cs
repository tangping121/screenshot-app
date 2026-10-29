using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ScreenshotApp.Services;

internal static class ClipboardImages
{
    const uint CfDib = 8;
    const uint GmemMoveable = 0x0002;

    public static void Set(BitmapSource source)
    {
        // 编码会冻结传入的图片。这里只用副本，标注窗口里的原图还要继续涂马赛克。
        BitmapSource image = source.Clone();
        if (image.Format != PixelFormats.Bgra32)
            image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        byte[] dib = BuildDib(image);
        byte[] png = EncodePng(image);
        uint pngFormat = RegisterClipboardFormat("PNG");

        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(40);
                continue;
            }

            try
            {
                if (!EmptyClipboard())
                    throw new InvalidOperationException("剪贴板正被其他程序占用。");
                Put(CfDib, dib);
                Put(pngFormat, png);
                return;
            }
            finally
            {
                CloseClipboard();
            }
        }

        throw new InvalidOperationException("剪贴板正被其他程序占用。");
    }

    static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    static byte[] BuildDib(BitmapSource image)
    {
        int width = image.PixelWidth;
        int height = image.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        image.CopyPixels(pixels, stride, 0);

        const int header = 40;
        byte[] dib = new byte[header + pixels.Length];
        WriteInt(dib, 0, header);
        WriteInt(dib, 4, width);
        WriteInt(dib, 8, height);
        WriteShort(dib, 12, 1);
        WriteShort(dib, 14, 32);
        WriteInt(dib, 20, pixels.Length);
        for (int y = 0; y < height; y++)
            Buffer.BlockCopy(pixels, y * stride, dib, header + (height - 1 - y) * stride, stride);
        return dib;
    }

    static void Put(uint format, byte[] bytes)
    {
        IntPtr memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero)
            throw new InvalidOperationException("无法写入剪贴板。");
        IntPtr locked = GlobalLock(memory);
        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(memory);
        }

        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            GlobalFree(memory);
            throw new InvalidOperationException("无法写入剪贴板。");
        }
    }

    static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    static void WriteShort(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GlobalFree(IntPtr hMem);
}
