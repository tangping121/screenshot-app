using System.ComponentModel;
using ScreenshotApp.Models;
using ScreenshotApp.Services;
using ScreenshotApp.Views;

namespace ScreenshotApp;

internal partial class MainWindow : Window
{
    readonly CaptureController _controller;

    public MainWindow(CaptureController controller)
    {
        _controller = controller;
        InitializeComponent();
        _controller.StateChanged += () => Dispatcher.Invoke(UpdateButtons);
        Reload();
        UpdateButtons();
    }

    public bool AllowClose { get; set; }

    public void Reload()
    {
        var settings = _controller.Settings;
        RegionShotButton.Content = "区域截图  " + settings.RegionShotHotkey;
        FullShotButton.Content = "全屏截图  " + settings.FullShotHotkey;
        RegionRecordButton.Content = "区域录像  " + settings.RegionRecordHotkey;
        StopButton.Content = "停止录像  " + settings.StopRecordHotkey;
        FolderText.Text = settings.SaveDirectory;
        HistoryList.ItemsSource = _controller.LoadHistory();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose && !_controller.ExitRequested)
        {
            e.Cancel = true;
            Hide();
            _controller.Notify("程序仍在托盘中运行。");
        }
        base.OnClosing(e);
    }

    void UpdateButtons()
    {
        bool locked = _controller.IsBusy;
        RegionShotButton.IsEnabled = !locked;
        FullShotButton.IsEnabled = !locked;
        RegionRecordButton.IsEnabled = !locked;
        FullRecordButton.IsEnabled = !locked;
        StopButton.IsEnabled = _controller.IsRecording;
    }

    void RegionShot_Click(object sender, RoutedEventArgs e) => _controller.StartRegionScreenshot();
    void FullShot_Click(object sender, RoutedEventArgs e) => _controller.StartFullScreenshot();
    void RegionRecord_Click(object sender, RoutedEventArgs e) => _controller.StartRegionRecording();
    void FullRecord_Click(object sender, RoutedEventArgs e) => _controller.StartFullRecording();
    void Stop_Click(object sender, RoutedEventArgs e) => _controller.StopRecording();

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_controller.Settings, _controller.TryApplySettings) { Owner = this };
        dialog.ShowDialog();
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) => _controller.OpenSaveFolder();

    void Open_Click(object sender, RoutedEventArgs e) => _controller.OpenEntry(Entry(sender));
    void Reveal_Click(object sender, RoutedEventArgs e) => _controller.RevealEntry(Entry(sender));
    void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { _controller.CopyEntry(Entry(sender)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "截图录像", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        try { _controller.DeleteEntry(Entry(sender)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "截图录像", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    static HistoryEntry Entry(object sender) => (HistoryEntry)((Button)sender).DataContext;
}
