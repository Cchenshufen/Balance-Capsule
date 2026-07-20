using QuotaOrb.Windows.Integration;

namespace QuotaOrb.Windows.Tests.Integration;

public sealed class WindowPlacementServiceTests
{
    private static readonly LogicalRect WorkArea = new(0, 0, 1920, 1040);
    private static readonly LogicalSize OrbSize = new(112, 112);

    [Fact]
    public void ClampAndSnap_ClampsBottomRightWithSafeInset()
    {
        var result = WindowPlacementService.ClampAndSnap(
            new LogicalPoint(1900, 1000),
            OrbSize,
            WorkArea,
            12);

        Assert.Equal(new LogicalPoint(1796, 916), result);
    }

    [Fact]
    public void ClampAndSnap_SnapsNearLeftEdge()
    {
        var result = WindowPlacementService.ClampAndSnap(
            new LogicalPoint(7, 200),
            OrbSize,
            WorkArea,
            12);

        Assert.Equal(new LogicalPoint(0, 200), result);
    }

    [Fact]
    public void ClampAndSnap_LeavesInteriorPointUnchanged()
    {
        var point = new LogicalPoint(640, 360);

        var result = WindowPlacementService.ClampAndSnap(point, OrbSize, WorkArea, 12);

        Assert.Equal(point, result);
    }

    [Fact]
    public void ClampAndSnap_RejectsInvalidGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowPlacementService.ClampAndSnap(
                new LogicalPoint(10, 10),
                new LogicalSize(0, 84),
                WorkArea,
                12));
    }
}
