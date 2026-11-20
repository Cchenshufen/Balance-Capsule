using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuotaOrb.Windows.Integration;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace QuotaOrb.Windows.Windows;

internal sealed class CapsuleWindow : Window
{
    private readonly DispatcherTimer _frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LogicalPoint _anchor;
    private double _animationFrom;
    private double _animationTo;
    private double _animationStarted;
    private double? _hideAt;
    private double? _openAt;
    private Point? _dragStart;
    private LogicalPoint _dragAnchor;
    private bool _dragMoved;
    private bool _positioned;
    private bool _menuOpen;

    internal CapsuleGlass Glass { get; }
    internal LogicalPoint OrbPosition => _anchor;
    internal Func<LogicalPoint?>? FindVisibleNeighbor { get; set; }
    internal event Action<LogicalPoint>? PositionCommitted;
    internal event Action? RefreshRequested;

    internal CapsuleWindow(bool claude, LogicalPoint origin, Func<System.Windows.Controls.ContextMenu> menu)
    {
        _anchor = origin;
        Glass = new CapsuleGlass { Claude = claude };
        Content = Glass;
        Width = CapsuleGlass.CompactWidth;
        Height = CapsuleGlass.CompactHeight;
        Left = origin.X;
        Top = origin.Y;
        Title = $"Balance Capsule · {Glass.SourceName}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = false;
        System.Windows.Automation.AutomationProperties.SetName(Glass, Title);

        MouseEnter += (_, _) => RequestOpen();
        MouseMove += OnPointerMoved;
        MouseLeave += (_, _) => RequestClose();
        PreviewMouseLeftButtonDown += OnPointerDown;
        PreviewMouseLeftButtonUp += OnPointerUp;
        LostMouseCapture += (_, _) =>
        {
            if (_dragStart is not null) FinishDrag();
        };
        PreviewMouseRightButtonUp += (_, e) =>
        {
            _menuOpen = true;
            var context = menu();
            context.PlacementTarget = Glass;
            context.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            context.Closed += (_, _) =>
            {
                _menuOpen = false;
                if (!IsMouseOver) RequestClose();
            };
            context.IsOpen = true;
            e.Handled = true;
        };
        _frame.Tick += (_, _) => UpdateFrame();
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                // No activation even when the glass is dragged or double-clicked.
                NativeCapsuleWindow.MakeNonActivating(source.Handle);
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                if (!_positioned)
                {
                    _positioned = true;
                    _anchor = Clamp(_anchor, 0);
                    PlaceCompact();
                }
                _frame.Start();
            }
            else
            {
                if (IsMouseCaptured) ReleaseMouseCapture();
                _frame.Stop();
                _openAt = _hideAt = null;
                _animationTo = Glass.Expansion = 0;
                PlaceCompact();
            }
        };
        Closed += (_, _) => _frame.Stop();
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    private void RequestOpen()
    {
        if (_dragStart is not null || _menuOpen) return;
        _hideAt = null;
        if (_animationTo == 0) _openAt ??= Now + .08;
    }

    private void RequestClose()
    {
        _openAt = null;
        if (_dragStart is null && !_menuOpen && Glass.IsExpanded) _hideAt ??= Now + .32;
    }

    private void UpdateFrame()
    {
        var now = Now;
        if (_openAt is { } opening && now >= opening)
        {
            _openAt = null;
            if (IsMouseOver && !_menuOpen && _dragStart is null) Animate(1);
        }

        if (_hideAt is { } hiding && now >= hiding)
        {
            _hideAt = null;
            if (!IsMouseOver && !_menuOpen && _dragStart is null) Animate(0);
        }

        if (Glass.Expansion != _animationTo)
        {
            var time = Math.Min(1, (now - _animationStarted) / (_animationTo == 1 ? 1.02 : .32));
            var eased = _animationTo == 1 ? Stage(time) : CapsuleGlass.Smooth(time);
            Glass.Expansion = time >= 1 ? _animationTo : _animationFrom + (_animationTo - _animationFrom) * eased;
            if (time >= 1 && _animationTo == 0) PlaceCompact();
        }

        Glass.Phase = now * (Glass.IsExpanded ? 1.05 : 1.65);
        Glass.InvalidateVisual();
    }

    private void Animate(double target)
    {
        if (target == _animationTo) return;
        _animationFrom = Glass.Expansion;
        _animationTo = target;
        _animationStarted = Now;
        if (target == 1 && !Glass.IsExpanded)
        {
            Glass.IsExpanded = true;
            PlaceExpanded();
        }
        else if (target == 0 && Glass.Expansion == 0)
        {
            PlaceCompact();
        }
    }

    private void PlaceCompact()
    {
        Glass.IsExpanded = false;
        Glass.OrbTop = 8;
        Width = CapsuleGlass.CompactWidth;
        Height = CapsuleGlass.CompactHeight;
        Left = _anchor.X;
        Top = _anchor.Y;
    }

    private void PlaceExpanded()
    {
        var work = WorkArea(_anchor);
        var rightCandidate = _anchor.X;
        var leftCandidate = _anchor.X - CapsuleGlass.ExpandedWidth + CapsuleGlass.CompactWidth;
        var canOpenLeft = leftCandidate >= work.Left;
        var canOpenRight = rightCandidate + CapsuleGlass.ExpandedWidth <= work.Right;
        // Preserve the sphere's position as it becomes the leading end of the card.
        Glass.OpensRight = canOpenRight
            || !canOpenLeft && work.Right - rightCandidate >= _anchor.X + CapsuleGlass.CompactWidth - work.Left;
        if (canOpenLeft && canOpenRight && FindVisibleNeighbor?.Invoke() is { } neighbor
            && Math.Abs(neighbor.Y - _anchor.Y) < CapsuleGlass.ExpandedHeight / 2 + 30)
        {
            Glass.OpensRight = neighbor.X < _anchor.X;
        }
        var top = Math.Clamp(_anchor.Y - 66, work.Top, Math.Max(work.Top, work.Bottom - CapsuleGlass.ExpandedHeight));
        Glass.OrbTop = _anchor.Y + 8 - top;
        Width = CapsuleGlass.ExpandedWidth;
        Height = CapsuleGlass.ExpandedHeight;
        Left = Glass.OpensRight ? rightCandidate : leftCandidate;
        Top = top;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(Glass);
        if (e.ClickCount == 2 && Glass.OrbRect.Contains(point))
        {
            RefreshRequested?.Invoke();
            e.Handled = true;
            return;
        }
        _openAt = _hideAt = null;
        _dragStart = Glass.PointToScreen(point);
        _dragAnchor = _anchor;
        _dragMoved = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            RequestOpen();
            return;
        }

        var point = Glass.PointToScreen(e.GetPosition(Glass));
        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (point.X - start.X) / dpi.DpiScaleX;
        var dy = (point.Y - start.Y) / dpi.DpiScaleY;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
        _dragMoved = true;
        _anchor = new LogicalPoint(_dragAnchor.X + dx, _dragAnchor.Y + dy);
        if (Glass.IsExpanded) PlaceExpanded();
        else PlaceCompact();
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        FinishDrag();
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    private void FinishDrag()
    {
        _dragStart = null;
        if (_dragMoved)
        {
            _anchor = Clamp(_anchor, 14);
            if (Glass.IsExpanded) PlaceExpanded();
            else PlaceCompact();
            PositionCommitted?.Invoke(_anchor);
        }

        if (IsMouseOver) RequestOpen();
        else RequestClose();
    }

    private LogicalPoint Clamp(LogicalPoint origin, double snap)
    {
        var work = WorkArea(origin);
        var right = Math.Max(work.Left, work.Right - CapsuleGlass.CompactWidth);
        var bottom = Math.Max(work.Top, work.Bottom - CapsuleGlass.CompactHeight);
        var x = Math.Clamp(origin.X, work.Left, right);
        var y = Math.Clamp(origin.Y, work.Top, bottom);
        if (x - work.Left < snap) x = work.Left;
        else if (right - x < snap) x = right;
        if (y - work.Top < snap) y = work.Top;
        else if (bottom - y < snap) y = bottom;
        return new(x, y);
    }

    private LogicalRect WorkArea(LogicalPoint point)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
            (int)Math.Round((point.X + CapsuleGlass.CompactWidth / 2) * dpi.DpiScaleX),
            (int)Math.Round((point.Y + CapsuleGlass.CompactHeight / 2) * dpi.DpiScaleY)));
        var work = screen.WorkingArea;
        return new(work.Left / dpi.DpiScaleX, work.Top / dpi.DpiScaleY, work.Width / dpi.DpiScaleX, work.Height / dpi.DpiScaleY);
    }

    private static double Stage(double time)
    {
        if (time < .24) return .16 * CapsuleGlass.Smooth(time / .24);
        if (time < .52) return .16 + .28 * CapsuleGlass.Smooth((time - .24) / .28);
        return .44 + .56 * (1 - Math.Pow(1 - (time - .52) / .48, 3));
    }
}

internal static partial class NativeCapsuleWindow
{
    private const int ExtendedStyle = -20;
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x00000080;

    internal static void MakeNonActivating(nint handle)
    {
        var style = GetWindowLong(handle, ExtendedStyle);
        _ = SetWindowLong(handle, ExtendedStyle, style | NoActivate | ToolWindow);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint handle, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
