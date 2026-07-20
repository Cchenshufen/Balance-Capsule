using QuotaOrb.Windows.Integration;

namespace QuotaOrb.Windows.Tests.Integration;

public sealed class FullscreenPolicyTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void IsFullscreen_WithExactMonitorCoverage_ReturnsTrue()
    {
        Assert.True(FullscreenPolicy.IsFullscreen(Monitor, Monitor, tolerance: 2));
    }

    [Fact]
    public void IsFullscreen_WithMaximizedWorkArea_ReturnsFalse()
    {
        var workAreaWindow = new PixelRect(0, 0, 1920, 1040);

        Assert.False(FullscreenPolicy.IsFullscreen(workAreaWindow, Monitor, tolerance: 2));
    }

    [Fact]
    public void IsFullscreen_OutsideTolerance_ReturnsFalse()
    {
        var almostFullscreen = new PixelRect(0, 0, 1917, 1080);

        Assert.False(FullscreenPolicy.IsFullscreen(almostFullscreen, Monitor, tolerance: 2));
    }

    [Theory]
    [InlineData(0, 0, 0, 1080)]
    [InlineData(0, 0, 1920, 0)]
    [InlineData(100, 100, 50, 50)]
    public void IsFullscreen_WithInvalidWindow_ReturnsFalse(
        int left,
        int top,
        int right,
        int bottom)
    {
        Assert.False(FullscreenPolicy.IsFullscreen(
            new PixelRect(left, top, right, bottom),
            Monitor,
            tolerance: 2));
    }
}
