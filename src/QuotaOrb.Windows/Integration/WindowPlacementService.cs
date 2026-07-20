using System.Windows.Media;

namespace QuotaOrb.Windows.Integration;

public readonly record struct LogicalPoint(double X, double Y);

public readonly record struct LogicalSize(double Width, double Height);

public readonly record struct LogicalRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public static class WindowPlacementService
{
    public static LogicalPoint ClampAndSnap(
        LogicalPoint candidate,
        LogicalSize window,
        LogicalRect workArea,
        double snapDistance)
    {
        Validate(candidate, window, workArea, snapDistance);

        var maximumLeft = Math.Max(workArea.Left, workArea.Right - window.Width);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - window.Height);
        var left = Math.Clamp(candidate.X, workArea.Left, maximumLeft);
        var top = Math.Clamp(candidate.Y, workArea.Top, maximumTop);

        if (Math.Abs(candidate.X - workArea.Left) <= snapDistance)
        {
            left = workArea.Left;
        }
        else if (candidate.X + window.Width >= workArea.Right - snapDistance)
        {
            left = Math.Max(workArea.Left, maximumLeft - snapDistance);
        }

        if (Math.Abs(candidate.Y - workArea.Top) <= snapDistance)
        {
            top = workArea.Top;
        }
        else if (candidate.Y + window.Height >= workArea.Bottom - snapDistance)
        {
            top = Math.Max(workArea.Top, maximumTop - snapDistance);
        }

        return new LogicalPoint(left, top);
    }

    public static LogicalPoint ClampAndSnapForWindow(
        System.Windows.Window owner,
        LogicalPoint candidate,
        LogicalSize window,
        double snapDistance)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dpi = VisualTreeHelper.GetDpi(owner);
        var devicePoint = new System.Drawing.Point(
            checked((int)Math.Round(candidate.X * dpi.DpiScaleX)),
            checked((int)Math.Round(candidate.Y * dpi.DpiScaleY)));
        var screen = System.Windows.Forms.Screen.FromPoint(devicePoint);
        var work = screen.WorkingArea;
        var logicalWork = new LogicalRect(
            work.Left / dpi.DpiScaleX,
            work.Top / dpi.DpiScaleY,
            work.Width / dpi.DpiScaleX,
            work.Height / dpi.DpiScaleY);

        return ClampAndSnap(candidate, window, logicalWork, snapDistance);
    }

    private static void Validate(
        LogicalPoint candidate,
        LogicalSize window,
        LogicalRect workArea,
        double snapDistance)
    {
        if (!double.IsFinite(candidate.X) || !double.IsFinite(candidate.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(candidate));
        }

        if (!double.IsFinite(window.Width) || window.Width <= 0 ||
            !double.IsFinite(window.Height) || window.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        if (!double.IsFinite(workArea.Left) || !double.IsFinite(workArea.Top) ||
            !double.IsFinite(workArea.Width) || workArea.Width <= 0 ||
            !double.IsFinite(workArea.Height) || workArea.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        if (!double.IsFinite(snapDistance) || snapDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(snapDistance));
        }
    }
}
