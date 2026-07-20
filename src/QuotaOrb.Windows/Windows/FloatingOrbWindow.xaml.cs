using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuotaOrb.Core.Settings;
using QuotaOrb.Windows.Integration;
using QuotaOrb.Windows.ViewModels;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace QuotaOrb.Windows.Windows;

public partial class FloatingOrbWindow : System.Windows.Window
{
    private readonly OrbViewModel _viewModel;
    private readonly DispatcherTimer _openTimer;
    private readonly DispatcherTimer _closeTimer;
    private DetailFlyoutWindow? _detailWindow;
    private Storyboard? _ambientStoryboard;
    private Storyboard? _errorPulseStoryboard;
    private Storyboard? _hoverInStoryboard;
    private Storyboard? _hoverOutStoryboard;
    private Storyboard? _stateWashStoryboard;
    private string? _lastPaletteKey;
    private bool? _lastCanAnimate;

    public FloatingOrbWindow(OrbViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;

        _openTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(200),
            DispatcherPriority.Background,
            OnOpenTimer,
            Dispatcher);
        _openTimer.Stop();
        _closeTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(300),
            DispatcherPriority.Background,
            OnCloseTimer,
            Dispatcher);
        _closeTimer.Stop();

        Loaded += OnLoaded;
        Closed += OnClosed;
        _viewModel.PropertyChanged += OnViewModelChanged;
        System.Windows.SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public event EventHandler<LogicalPoint>? PositionCommitted;

    public event EventHandler? RefreshRequested;

    public event EventHandler<bool>? StartupToggleRequested;

    public event EventHandler<AgentSource>? AgentSourceRequested;

    public event EventHandler<QuotaWindowMode>? QuotaWindowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void AttachDetailWindow(DetailFlyoutWindow detailWindow)
    {
        ArgumentNullException.ThrowIfNull(detailWindow);

        if (_detailWindow is not null)
        {
            _detailWindow.MouseEnter -= OnDetailHoverEntered;
            _detailWindow.MouseLeave -= OnDetailHoverLeft;
        }

        _detailWindow = detailWindow;
        _detailWindow.MouseEnter += OnDetailHoverEntered;
        _detailWindow.MouseLeave += OnDetailHoverLeft;
    }

    public void SetStartupEnabled(bool enabled) => StartupMenuItem.IsChecked = enabled;

    public void SetSelectedAgent(AgentSource source)
    {
        CodexAgentMenuItem.IsChecked = source == AgentSource.Codex;
        ClaudeAgentMenuItem.IsChecked = source == AgentSource.Claude;
        ClaudeCodeAgentMenuItem.IsChecked = source == AgentSource.ClaudeCode;
    }

    public void SetSelectedQuotaWindow(QuotaWindowMode mode)
    {
        FiveHourQuotaMenuItem.IsChecked = mode == QuotaWindowMode.FiveHour;
        WeeklyQuotaMenuItem.IsChecked = mode == QuotaWindowMode.Weekly;
    }

    public void SetQuotaWindowAvailability(
        bool quotaAvailable,
        bool fiveHourAvailable,
        bool weeklyAvailable)
    {
        QuotaWindowMenuItem.IsEnabled = quotaAvailable;
        FiveHourQuotaMenuItem.IsEnabled = fiveHourAvailable;
        WeeklyQuotaMenuItem.IsEnabled = weeklyAvailable;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_detailWindow is not null && _detailWindow.Owner is null)
        {
            _detailWindow.Owner = this;
        }

        UpdateAnimation();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        _ambientStoryboard?.Remove(this);
        _errorPulseStoryboard?.Remove(this);
        _hoverInStoryboard?.Remove(this);
        _hoverOutStoryboard?.Remove(this);
        _stateWashStoryboard?.Remove(this);
        _viewModel.PropertyChanged -= OnViewModelChanged;
        System.Windows.SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        if (_detailWindow is not null)
        {
            _detailWindow.MouseEnter -= OnDetailHoverEntered;
            _detailWindow.MouseLeave -= OnDetailHoverLeft;
            _detailWindow.Close();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var paletteChanged = !string.Equals(
            _lastPaletteKey,
            _viewModel.PaletteKey,
            StringComparison.Ordinal);
        UpdateAnimation();
        if (paletteChanged)
        {
            RunStateTransition();
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) =>
        UpdateAnimation();

    private void UpdateAnimation()
    {
        if (!IsLoaded)
        {
            return;
        }

        var canAnimate = _viewModel.CanAnimate;
        if (_lastCanAnimate == canAnimate &&
            string.Equals(_lastPaletteKey, _viewModel.PaletteKey, StringComparison.Ordinal))
        {
            return;
        }

        _ambientStoryboard ??= (Storyboard)FindResource("AmbientMotionStoryboard");
        _errorPulseStoryboard ??= (Storyboard)FindResource("ErrorPulseStoryboard");
        _ambientStoryboard.Remove(this);
        _errorPulseStoryboard.Remove(this);
        _hoverInStoryboard?.Remove(this);
        _hoverOutStoryboard?.Remove(this);
        ResetStaticTransforms();

        if (canAnimate)
        {
            if (_viewModel.HasError)
            {
                _errorPulseStoryboard.Begin(this, isControllable: true);
            }
            else
            {
                _ambientStoryboard.Begin(this, isControllable: true);
            }
        }

        _lastCanAnimate = canAnimate;
        _lastPaletteKey = _viewModel.PaletteKey;
    }

    private void ResetStaticTransforms()
    {
        OrbScale.ScaleX = 1;
        OrbScale.ScaleY = 1;
        OrbRotate.Angle = -0.6;
        OrbTranslateY.Y = 2;
        GroundShadowScale.ScaleX = 0.78;
        GroundShadowScale.ScaleY = 0.78;
        GroundShadow.Opacity = 0.5;
        AuraScale.ScaleX = 0.96;
        AuraScale.ScaleY = 0.96;
        AuraRing.Opacity = 0.16;
        FrontLiquidTranslate.X = -7;
        BackLiquidTranslate.X = 7;
        FrontLiquidRotate.Angle = -2;
        BackLiquidRotate.Angle = 2;
        Bubble1Translate.Y = 10;
        Bubble2Translate.Y = 10;
        Bubble3Translate.Y = 10;
        SweepTranslate.X = -80;
        StateWash.Opacity = 0.1;
    }

    private void RunStateTransition()
    {
        if (!IsLoaded || !_viewModel.CanAnimate)
        {
            StateWash.Opacity = 0.1;
            return;
        }

        _stateWashStoryboard ??= (Storyboard)FindResource("StateWashStoryboard");
        _stateWashStoryboard.Remove(this);
        _stateWashStoryboard.Begin(this, isControllable: true);
    }

    private void SetHoverState(bool hovered)
    {
        if (!_viewModel.CanAnimate)
        {
            OrbScale.ScaleX = hovered ? 1.04 : 1;
            OrbScale.ScaleY = hovered ? 1.04 : 1;
            return;
        }

        _hoverInStoryboard ??= (Storyboard)FindResource("HoverInStoryboard");
        _hoverOutStoryboard ??= (Storyboard)FindResource("HoverOutStoryboard");
        var storyboard = hovered ? _hoverInStoryboard : _hoverOutStoryboard;
        storyboard.Begin(this, isControllable: true);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _openTimer.Stop();
            _closeTimer.Stop();
            RefreshRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        _detailWindow?.HideAnimated();
        _openTimer.Stop();
        _closeTimer.Stop();

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var snapped = WindowPlacementService.ClampAndSnapForWindow(
            this,
            new LogicalPoint(Left, Top),
            new LogicalSize(OrbVisualMetrics.HostSize, OrbVisualMetrics.HostSize),
            snapDistance: 12);
        Left = snapped.X;
        Top = snapped.Y;
        PositionCommitted?.Invoke(this, snapped);
    }

    private void OnHoverEntered(object sender, WpfMouseEventArgs e)
    {
        SetHoverState(hovered: true);
        _closeTimer.Stop();
        if (_detailWindow?.IsVisible != true)
        {
            _openTimer.Stop();
            _openTimer.Start();
        }
    }

    private void OnHoverLeft(object sender, WpfMouseEventArgs e)
    {
        SetHoverState(hovered: false);
        ScheduleClose();
    }

    private void OnDetailHoverEntered(object sender, WpfMouseEventArgs e)
    {
        _openTimer.Stop();
        _closeTimer.Stop();
    }

    private void OnDetailHoverLeft(object sender, WpfMouseEventArgs e) => ScheduleClose();

    private void ScheduleClose()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        _closeTimer.Start();
    }

    private void OnOpenTimer(object? sender, EventArgs e)
    {
        _openTimer.Stop();
        if (!OrbHitTarget.IsMouseOver || _detailWindow is null)
        {
            return;
        }

        PositionDetailWindow(_detailWindow);
        if (!_detailWindow.IsVisible)
        {
            _detailWindow.ShowAnimated();
        }
    }

    private void OnCloseTimer(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        if (!OrbHitTarget.IsMouseOver && _detailWindow?.IsMouseOver != true)
        {
            _detailWindow?.HideAnimated();
        }
    }

    private void PositionDetailWindow(DetailFlyoutWindow detail)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var devicePoint = new System.Drawing.Point(
            checked((int)Math.Round(Left * dpi.DpiScaleX)),
            checked((int)Math.Round(Top * dpi.DpiScaleY)));
        var work = System.Windows.Forms.Screen.FromPoint(devicePoint).WorkingArea;
        var workLeft = work.Left / dpi.DpiScaleX;
        var workTop = work.Top / dpi.DpiScaleY;
        var workRight = work.Right / dpi.DpiScaleX;
        var workBottom = work.Bottom / dpi.DpiScaleY;
        var gap = -11d;

        var proposedRight = Left + ActualWidth + gap;
        detail.Left = proposedRight + detail.Width <= workRight
            ? proposedRight
            : Math.Max(workLeft, Left - detail.Width - gap);
        detail.Top = Math.Clamp(
            Top + (ActualHeight - detail.Height) / 2d,
            workTop,
            Math.Max(workTop, workBottom - detail.Height));
    }

    private void OnRefreshClicked(object sender, System.Windows.RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnStartupClicked(object sender, System.Windows.RoutedEventArgs e) =>
        StartupToggleRequested?.Invoke(this, !StartupMenuItem.IsChecked);

    private void OnCodexAgentClicked(object sender, System.Windows.RoutedEventArgs e) =>
        AgentSourceRequested?.Invoke(this, AgentSource.Codex);

    private void OnClaudeAgentClicked(object sender, System.Windows.RoutedEventArgs e) =>
        AgentSourceRequested?.Invoke(this, AgentSource.Claude);

    private void OnClaudeCodeAgentClicked(object sender, System.Windows.RoutedEventArgs e) =>
        AgentSourceRequested?.Invoke(this, AgentSource.ClaudeCode);

    private void OnFiveHourQuotaClicked(object sender, System.Windows.RoutedEventArgs e) =>
        QuotaWindowRequested?.Invoke(this, QuotaWindowMode.FiveHour);

    private void OnWeeklyQuotaClicked(object sender, System.Windows.RoutedEventArgs e) =>
        QuotaWindowRequested?.Invoke(this, QuotaWindowMode.Weekly);

    private void OnSettingsClicked(object sender, System.Windows.RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClicked(object sender, System.Windows.RoutedEventArgs e) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}
