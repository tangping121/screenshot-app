using System.Windows.Interop;

namespace ScreenshotApp.Services;

internal sealed class HotkeyGesture
{
    public HotkeyGesture(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public ModifierKeys Modifiers { get; }
    public Key Key { get; }

    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Control))
                parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                parts.Add("Win");
            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }
    }

    public uint NativeModifiers
    {
        get
        {
            uint value = NativeMethods.ModNoRepeat;
            if (Modifiers.HasFlag(ModifierKeys.Alt))
                value |= NativeMethods.ModAlt;
            if (Modifiers.HasFlag(ModifierKeys.Control))
                value |= NativeMethods.ModControl;
            if (Modifiers.HasFlag(ModifierKeys.Shift))
                value |= NativeMethods.ModShift;
            if (Modifiers.HasFlag(ModifierKeys.Windows))
                value |= NativeMethods.ModWin;
            return value;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "快捷键不能为空。";
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "快捷键至少要包含 Ctrl、Alt 或 Shift，例如 Ctrl+Alt+A。";
            return false;
        }

        ModifierKeys modifiers = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    error = $"无法识别的修饰键：{parts[i]}";
                    return false;
            }
        }

        if (modifiers == ModifierKeys.None)
        {
            error = "快捷键至少要包含 Ctrl、Alt 或 Shift。";
            return false;
        }

        if (!TryParseKey(parts[^1], out Key key))
        {
            error = $"无法识别的按键：{parts[^1]}";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static bool TryFromInput(KeyEventArgs e, out HotkeyGesture? gesture)
    {
        gesture = null;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin
            or Key.None or Key.System)
            return false;

        ModifierKeys modifiers = ModifierKeys.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            modifiers |= ModifierKeys.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            modifiers |= ModifierKeys.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            modifiers |= ModifierKeys.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            modifiers |= ModifierKeys.Windows;
        if (modifiers == ModifierKeys.None)
            return false;

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    static bool TryParseKey(string text, out Key key)
    {
        if (text.Length == 1 && char.IsDigit(text[0]))
            text = "D" + text;
        if (text.Equals("Esc", StringComparison.OrdinalIgnoreCase))
            text = "Escape";
        return Enum.TryParse(text, true, out key) && key != Key.None;
    }

    static string FormatKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((int)(key - Key.D0)).ToString();
        if (key == Key.Escape)
            return "Esc";
        return key.ToString();
    }
}
