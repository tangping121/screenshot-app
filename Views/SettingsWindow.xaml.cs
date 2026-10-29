using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class SettingsWindow : Window
{
    readonly Func<AppSettings, string?> _apply;

    public SettingsWindow(AppSettings settings, Func<AppSettings, string?> apply)
    {
        _apply = apply;
        InitializeComponent();
        FolderBox.Text = settings.SaveDirectory;
        RegionShotBox.Text = settings.RegionShotHotkey;
        FullShotBox.Text = settings.FullShotHotkey;
        RegionRecordBox.Text = settings.RegionRecordHotkey;
        StopRecordBox.Text = settings.StopRecordHotkey;
        Hook(RegionShotBox);
        Hook(FullShotBox);
        Hook(RegionRecordBox);
        Hook(StopRecordBox);

        CapBox.Items.Add(new CapItem(ResolutionCap.Native));
        CapBox.Items.Add(new CapItem(ResolutionCap.P1080));
        CapBox.Items.Add(new CapItem(ResolutionCap.P2K));
        ResolutionCap cap = ResolutionCaps.Parse(settings.ResolutionCap);
        CapBox.SelectedItem = CapBox.Items.Cast<CapItem>().First(item => item.Cap == cap);
        FpsBox.Items.Add(30);
        FpsBox.Items.Add(60);
        FpsBox.SelectedItem = settings.FrameRate == 60 ? 60 : 30;
        SystemBox.IsChecked = settings.SystemAudio;
        MicBox.IsChecked = settings.Microphone;
        CountdownBox.IsChecked = settings.CountdownEnabled;

        var probe = RecordingService.ProbeAudio();
        DeviceText.Text = string.Join(Environment.NewLine, new[] { probe.SystemReason, probe.MicrophoneReason }.Where(text => text.Length > 0));
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择保存目录",
            UseDescriptionForTitle = true,
            SelectedPath = FolderBox.Text
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FolderBox.Text = dialog.SelectedPath;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        string[] hotkeys = { RegionShotBox.Text, FullShotBox.Text, RegionRecordBox.Text, StopRecordBox.Text };
        foreach (string hotkey in hotkeys)
        {
            if (!HotkeyGesture.TryParse(hotkey, out _, out string error))
            {
                MessageBox.Show(error, "快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (hotkeys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != hotkeys.Length)
        {
            MessageBox.Show("四个快捷键不能重复。", "快捷键", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var next = new AppSettings
        {
            SaveDirectory = FolderBox.Text.Trim(),
            RegionShotHotkey = RegionShotBox.Text.Trim(),
            FullShotHotkey = FullShotBox.Text.Trim(),
            RegionRecordHotkey = RegionRecordBox.Text.Trim(),
            StopRecordHotkey = StopRecordBox.Text.Trim(),
            FrameRate = FpsBox.SelectedItem is int fps && fps == 60 ? 60 : 30,
            ResolutionCap = ResolutionCaps.ToSetting((CapBox.SelectedItem as CapItem)?.Cap ?? ResolutionCap.Native),
            SystemAudio = SystemBox.IsChecked == true,
            Microphone = MicBox.IsChecked == true,
            CountdownEnabled = CountdownBox.IsChecked == true
        };

        string? applyError = _apply(next);
        if (applyError != null)
        {
            MessageBox.Show(applyError, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    static void Hook(TextBox box)
    {
        box.IsReadOnly = true;
        box.PreviewKeyDown += (_, e) =>
        {
            if (HotkeyGesture.TryFromInput(e, out HotkeyGesture? gesture) && gesture != null)
            {
                box.Text = gesture.Text;
                e.Handled = true;
            }
            else if (e.Key is not (Key.Tab or Key.Escape or Key.Enter))
            {
                e.Handled = true;
            }
        };
    }

    sealed class CapItem
    {
        public CapItem(ResolutionCap cap) => Cap = cap;
        public ResolutionCap Cap { get; }
        public override string ToString() => ResolutionCaps.Label(Cap);
    }
}
