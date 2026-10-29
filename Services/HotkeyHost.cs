using System.Windows.Interop;
using ScreenshotApp.Models;

namespace ScreenshotApp.Services;

internal sealed class HotkeyHost : IDisposable
{
    readonly Window _window;
    readonly CaptureController _controller;
    HwndSource? _source;
    readonly List<int> _registered = new();

    public HotkeyHost(Window window, CaptureController controller)
    {
        _window = window;
        _controller = controller;
    }

    public void Attach()
    {
        _source = (HwndSource?)PresentationSource.FromVisual(_window);
        if (_source == null)
            throw new InvalidOperationException("主窗口还没有窗口句柄。");
        _source.AddHook(Hook);
    }

    public string? Apply(AppSettings settings)
    {
        if (_source == null)
            return "快捷键还不能注册。";

        Unregister();
        (int Id, string Text, Action Action)[] bindings =
        {
            (1, settings.RegionShotHotkey, _controller.StartRegionScreenshot),
            (2, settings.FullShotHotkey, _controller.StartFullScreenshot),
            (3, settings.RegionRecordHotkey, _controller.StartRegionRecording),
            (4, settings.StopRecordHotkey, _controller.StopRecording)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((int id, string text, Action action) in bindings)
        {
            if (!HotkeyGesture.TryParse(text, out HotkeyGesture? gesture, out string error) || gesture == null)
            {
                Unregister();
                return error;
            }

            if (!seen.Add(gesture.Text))
            {
                Unregister();
                return $"快捷键冲突：{gesture.Text} 被使用了多次。";
            }

            if (!NativeMethods.RegisterHotKey(_source.Handle, id, gesture.NativeModifiers, gesture.VirtualKey))
            {
                Unregister();
                return $"快捷键 {gesture.Text} 已被其他程序占用。";
            }

            _registered.Add(id);
            _actions[id] = action;
        }

        return null;
    }

    readonly Dictionary<int, Action> _actions = new();

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out Action? action))
        {
            handled = true;
            _window.Dispatcher.BeginInvoke(action);
        }

        return IntPtr.Zero;
    }

    void Unregister()
    {
        if (_source == null)
            return;
        foreach (int id in _registered)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _actions.Clear();
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(Hook);
    }
}
