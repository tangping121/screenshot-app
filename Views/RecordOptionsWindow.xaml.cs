using ScreenshotApp.Models;
using ScreenshotApp.Services;

namespace ScreenshotApp.Views;

public partial class RecordOptionsWindow : Window
{
    public RecordOptionsWindow(AppSettings settings)
    {
        InitializeComponent();
        Options.Load(settings);
    }

    public RecordChoice? Selected { get; private set; }

    public static RecordChoice? Ask(Window? owner, AppSettings settings)
    {
        var dialog = new RecordOptionsWindow(settings);
        if (owner != null && owner.IsVisible)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true ? dialog.Selected : null;
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        Selected = Options.Choice;
        DialogResult = true;
    }
}
