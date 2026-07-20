namespace QuotaOrb.Windows.Integration;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsValid => Width > 0 && Height > 0;
}

public static class FullscreenPolicy
{
    public static bool IsFullscreen(
        PixelRect windowRect,
        PixelRect monitorRect,
        int tolerance)
    {
        if (!windowRect.IsValid || !monitorRect.IsValid || tolerance < 0)
        {
            return false;
        }

        return Math.Abs(windowRect.Left - monitorRect.Left) <= tolerance &&
               Math.Abs(windowRect.Top - monitorRect.Top) <= tolerance &&
               Math.Abs(windowRect.Right - monitorRect.Right) <= tolerance &&
               Math.Abs(windowRect.Bottom - monitorRect.Bottom) <= tolerance;
    }
}
