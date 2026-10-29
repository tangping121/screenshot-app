using System.Runtime.InteropServices;
using ScreenshotApp.Models;
using ScreenRecorderLib;

namespace ScreenshotApp.Services;

internal static class ScreenshotService
{
    public static async Task<BitmapSource> CaptureAsync(MonitorInfo monitor, PixelRect region)
    {
        PixelRect clipped = region.Intersect(monitor.Bounds);
        if (clipped.Width < 4 || clipped.Height < 4)
            throw new InvalidOperationException("选区太小。");

        try
        {
            BitmapSource captured = await CaptureWithGraphicsCaptureAsync(monitor, clipped);
            return CropIfNeeded(captured, clipped, monitor);
        }
        catch (Exception ex)
        {
            Log.Info("Windows Graphics Capture 截图失败，改用 GDI：" + ex.Message);
            Log.Error(ex);
            return CopyWithGdi(clipped);
        }
    }

    static void Release(Recorder recorder)
    {
        try { recorder.Dispose(); }
        catch (MemberAccessException) { /* 库没有公开 Dispose 时交给终结器 */ }
        catch (Exception ex) { Log.Error(ex); }
    }

    static async Task<BitmapSource> CaptureWithGraphicsCaptureAsync(MonitorInfo monitor, PixelRect region)
    {
        var display = new DisplayRecordingSource(monitor.DeviceName)
        {
            RecorderApi = RecorderApi.WindowsGraphicsCapture,
            IsCursorCaptureEnabled = false,
            IsBorderRequired = false
        };

        int relX = region.X - monitor.Bounds.X;
        int relY = region.Y - monitor.Bounds.Y;
        bool full = relX <= 1 && relY <= 1
            && region.Width >= monitor.Bounds.Width - 1
            && region.Height >= monitor.Bounds.Height - 1;
        if (!full)
            display.SourceRect = new ScreenRect(relX, relY, region.Width, region.Height);

        RecorderOptions options = RecorderOptions.Default;
        options.SourceOptions.RecordingSources.Add(display);
        options.OutputOptions.RecorderMode = RecorderMode.Screenshot;
        options.OutputOptions.Stretch = StretchMode.None;
        options.SnapshotOptions.SnapshotFormat = ImageFormat.PNG;
        options.AudioOptions.IsAudioEnabled = false;
        options.MouseOptions.IsMousePointerEnabled = false;
        options.MouseOptions.IsMouseClicksDetected = false;

        string temp = Path.Combine(Path.GetTempPath(), $"shot-{Guid.NewGuid():N}.png");
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Recorder? recorder = Recorder.CreateRecorder(options);
        recorder.OnRecordingComplete += (_, e) => done.TrySetResult(e.FilePath ?? temp);
        recorder.OnRecordingFailed += (_, e) => done.TrySetException(new InvalidOperationException(string.IsNullOrWhiteSpace(e.Error) ? "截图失败。" : e.Error));

        try
        {
            recorder.Record(temp);
            Task finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(4)));
            if (finished != done.Task)
            {
                try { recorder.Stop(); } catch { /* 超时后放弃这一帧 */ }
                throw new TimeoutException("截图超时。");
            }

            string path = await done.Task;
            Release(recorder);
            recorder = null;
            if (!File.Exists(path))
                throw new FileNotFoundException("没有生成截图文件。", path);

            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            try { File.Delete(path); } catch { /* 临时文件删不掉不影响结果 */ }
            return image;
        }
        finally
        {
            if (recorder != null)
                Release(recorder);
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* 已由完成回调删过 */ }
            }
        }
    }

    static BitmapSource CropIfNeeded(BitmapSource source, PixelRect region, MonitorInfo monitor)
    {
        if (Math.Abs(source.PixelWidth - region.Width) <= 2 && Math.Abs(source.PixelHeight - region.Height) <= 2)
            return source;

        int relX = region.X - monitor.Bounds.X;
        int relY = region.Y - monitor.Bounds.Y;
        if (relX < 0 || relY < 0 || relX + region.Width > source.PixelWidth || relY + region.Height > source.PixelHeight)
            return source;

        var cropped = new CroppedBitmap(source, new Int32Rect(relX, relY, region.Width, region.Height));
        cropped.Freeze();
        return cropped;
    }

    public static BitmapSource CopyWithGdi(PixelRect region)
    {
        using var bitmap = new System.Drawing.Bitmap(
            region.Width,
            region.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                region.X,
                region.Y,
                0,
                0,
                new System.Drawing.Size(region.Width, region.Height),
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            bitmap.PixelFormat);
        try
        {
            int length = data.Stride * bitmap.Height;
            byte[] buffer = new byte[length];
            Marshal.Copy(data.Scan0, buffer, 0, length);
            var source = BitmapSource.Create(
                bitmap.Width,
                bitmap.Height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                buffer,
                data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
