using QuotaOrb.Windows.ViewModels;
using System.Windows.Media.Animation;

namespace QuotaOrb.Windows.Windows;

public partial class DetailFlyoutWindow : System.Windows.Window
{
    private readonly OrbViewModel _viewModel;
    private Storyboard? _enterStoryboard;
    private Storyboard? _exitStoryboard;
    private EventHandler? _exitCompletedHandler;

    public DetailFlyoutWindow(OrbViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    public void ShowAnimated()
    {
        CancelPendingExit();
        if (!IsVisible)
        {
            Show();
        }

        if (!_viewModel.CanAnimate)
        {
            FlyoutRoot.Opacity = 1d;
            FlyoutTranslate.Y = 0d;
            return;
        }

        _enterStoryboard ??= (Storyboard)FindResource("FlyoutEnterStoryboard");
        _enterStoryboard.Remove(this);
        _enterStoryboard.Begin(this, isControllable: true);
    }

    public void HideAnimated()
    {
        if (!IsVisible)
        {
            return;
        }

        _enterStoryboard?.Remove(this);
        if (!_viewModel.CanAnimate)
        {
            Hide();
            return;
        }

        CancelPendingExit();
        _exitStoryboard ??= (Storyboard)FindResource("FlyoutExitStoryboard");
        _exitCompletedHandler = (_, _) =>
        {
            if (_exitStoryboard is not null && _exitCompletedHandler is not null)
            {
                _exitStoryboard.Completed -= _exitCompletedHandler;
            }

            _exitCompletedHandler = null;
            Hide();
        };
        _exitStoryboard.Completed += _exitCompletedHandler;
        _exitStoryboard.Begin(this, isControllable: true);
    }

    private void CancelPendingExit()
    {
        if (_exitStoryboard is null)
        {
            return;
        }

        _exitStoryboard.Remove(this);
        if (_exitCompletedHandler is not null)
        {
            _exitStoryboard.Completed -= _exitCompletedHandler;
            _exitCompletedHandler = null;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _enterStoryboard?.Remove(this);
        CancelPendingExit();
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        LiquidGlassInterop.TryEnable(this);
}
