using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace QuotaOrb.Windows.Integration;

public sealed class FullscreenMonitor : IDisposable
{
    private const uint MonitorDefaultToNearest = 2;
    private readonly DispatcherTimer _timer;
    private readonly uint _currentProcessId;
    private bool _isFullscreen;
    private bool _disposed;

    public FullscreenMonitor(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _currentProcessId = checked((uint)Environment.ProcessId);
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(500),
            DispatcherPriority.Background,
            OnTick,
            dispatcher);
    }

    public event EventHandler<bool>? FullscreenChanged;

    public bool IsFullscreen => _isFullscreen;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EvaluateAndPublish();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _disposed = true;
    }

    private void OnTick(object? sender, EventArgs e) => EvaluateAndPublish();

    private void EvaluateAndPublish()
    {
        var next = TryDetectFullscreen();
        if (next == _isFullscreen)
        {
            return;
        }

        _isFullscreen = next;
        FullscreenChanged?.Invoke(this, next);
    }

    private bool TryDetectFullscreen()
    {
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero ||
                foreground == GetShellWindow() ||
                foreground == GetDesktopWindow())
            {
                return false;
            }

            _ = GetWindowThreadProcessId(foreground, out var processId);
            if (processId == 0 || processId == _currentProcessId)
            {
                return false;
            }

            if (!GetWindowRect(foreground, out var window))
            {
                return false;
            }

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            var info = new MonitorInfo
            {
                Size = checked((uint)Marshal.SizeOf<MonitorInfo>())
            };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return false;
            }

            return FullscreenPolicy.IsFullscreen(
                window.ToPixelRect(),
                info.Monitor.ToPixelRect(),
                tolerance: 2);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
