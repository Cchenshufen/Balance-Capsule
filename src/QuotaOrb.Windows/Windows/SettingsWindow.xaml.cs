using QuotaOrb.Core.Settings;

namespace QuotaOrb.Windows.Windows;

public partial class SettingsWindow : System.Windows.Window
{
    private readonly AppSettings _original;

    public SettingsWindow(AppSettings settings)
    {
        _original = settings ?? throw new ArgumentNullException(nameof(settings));
        InitializeComponent();
        StartWithWindowsCheckBox.IsChecked = settings.StartWithWindows;
        HideInFullscreenCheckBox.IsChecked = settings.HideInFullscreen;
        AnimationsEnabledCheckBox.IsChecked = settings.AnimationsEnabled;
        UpdatedSettings = settings;
    }

    public AppSettings UpdatedSettings { get; private set; }

    public event EventHandler<AppSettings>? SettingsConfirmed;

    private void OnDoneClicked(object sender, System.Windows.RoutedEventArgs e)
    {
        UpdatedSettings = _original with
        {
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
            HideInFullscreen = HideInFullscreenCheckBox.IsChecked == true,
            AnimationsEnabled = AnimationsEnabledCheckBox.IsChecked == true
        };
        SettingsConfirmed?.Invoke(this, UpdatedSettings);
        Close();
    }
}
