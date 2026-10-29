using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class RecordOptionsBar : UserControl
{
    AudioProbe _probe = new(true, true, "", "");

    public RecordOptionsBar()
    {
        InitializeComponent();
        CapBox.Items.Add(new CapChoice(ResolutionCaps.Label(ResolutionCap.Native), ResolutionCap.Native));
        CapBox.Items.Add(new CapChoice(ResolutionCaps.Label(ResolutionCap.P1080), ResolutionCap.P1080));
        CapBox.Items.Add(new CapChoice(ResolutionCaps.Label(ResolutionCap.P2K), ResolutionCap.P2K));
        FpsBox.Items.Add(30);
        FpsBox.Items.Add(60);
        CapBox.SelectionChanged += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
        FpsBox.SelectionChanged += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
        SystemBox.Checked += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
        SystemBox.Unchecked += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
        MicBox.Checked += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
        MicBox.Unchecked += (_, _) => ChoiceChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ChoiceChanged;

    public void Load(AppSettings settings)
    {
        _probe = RecordingService.ProbeAudio();
        ResolutionCap cap = ResolutionCaps.Parse(settings.ResolutionCap);
        CapBox.SelectedItem = CapBox.Items.Cast<CapChoice>().First(item => item.Value == cap);
        FpsBox.SelectedItem = settings.FrameRate == 60 ? 60 : 30;
        SystemBox.IsChecked = settings.SystemAudio && _probe.HasSystem;
        SystemBox.IsEnabled = _probe.HasSystem;
        SystemBox.ToolTip = _probe.SystemReason;
        MicBox.IsChecked = settings.Microphone && _probe.HasMicrophone;
        MicBox.IsEnabled = _probe.HasMicrophone;
        MicBox.ToolTip = _probe.MicrophoneReason;
        ReasonText.Text = string.Join("  ", new[] { _probe.SystemReason, _probe.MicrophoneReason }.Where(text => text.Length > 0));
    }

    public RecordChoice Choice => new()
    {
        Cap = (CapBox.SelectedItem as CapChoice)?.Value ?? ResolutionCap.Native,
        FrameRate = FpsBox.SelectedItem is int fps && fps == 60 ? 60 : 30,
        SystemAudio = SystemBox.IsChecked == true && SystemBox.IsEnabled,
        Microphone = MicBox.IsChecked == true && MicBox.IsEnabled
    };

    sealed class CapChoice(string label, ResolutionCap value)
    {
        public string Label { get; } = label;
        public ResolutionCap Value { get; } = value;
        public override string ToString() => Label;
    }
}
