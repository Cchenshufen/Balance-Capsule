using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Policy;

namespace QuotaOrb.Core.Tests.Policy;

public sealed class QuotaRiskPolicyTests
{
    [Theory]
    [InlineData(100, QuotaRisk.Safe)]
    [InlineData(41, QuotaRisk.Safe)]
    [InlineData(40, QuotaRisk.Warning)]
    [InlineData(20, QuotaRisk.Warning)]
    [InlineData(19, QuotaRisk.Critical)]
    [InlineData(0, QuotaRisk.Critical)]
    public void Evaluate_UsesFixedThresholds(double remaining, QuotaRisk expected)
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(100 - remaining, TimeSpan.FromHours(5), now.AddHours(1)),
            null,
            now);

        var result = QuotaRiskPolicy.Evaluate(snapshot, null, now);

        Assert.Equal(expected, result.Risk);
        Assert.Equal((int)remaining, result.DisplayPercent);
    }

    [Fact]
    public void Evaluate_UsesLowerRemainingWindow()
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(14, TimeSpan.FromHours(5), now.AddHours(4)),
            new QuotaWindow(38, TimeSpan.FromDays(7), now.AddDays(5)),
            now);

        var result = QuotaRiskPolicy.Evaluate(snapshot, null, now);

        Assert.Equal(62, result.DisplayPercent);
        Assert.Equal(QuotaRisk.Safe, result.Risk);
    }

    [Fact]
    public void Evaluate_WithErrorAndNoSnapshot_ShowsError()
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");
        var error = new QuotaReadError("timeout", "Codex request timed out.");

        var result = QuotaRiskPolicy.Evaluate(null, error, now);

        Assert.Equal(QuotaRisk.Error, result.Risk);
        Assert.Null(result.DisplayPercent);
        Assert.Equal(error, result.Error);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void Evaluate_WithErrorAndSnapshot_ReturnsStaleSnapshot()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-13T11:58:00+08:00");
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(14, TimeSpan.FromHours(5), now.AddHours(4)),
            new QuotaWindow(38, TimeSpan.FromDays(7), now.AddDays(5)),
            fetchedAt);
        var error = new QuotaReadError("timeout", "Codex request timed out.");

        var result = QuotaRiskPolicy.Evaluate(snapshot, error, now);

        Assert.Equal(QuotaRisk.Safe, result.Risk);
        Assert.Equal(62, result.DisplayPercent);
        Assert.Equal(snapshot.Current, result.Current);
        Assert.Equal(snapshot.Weekly, result.Weekly);
        Assert.Equal(fetchedAt, result.UpdatedAt);
        Assert.Equal(error, result.Error);
        Assert.True(result.IsStale);
    }

    [Fact]
    public void Evaluate_WithNoWindows_ShowsNoLimitsError()
    {
        var now = DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");
        var snapshot = new QuotaSnapshot(null, null, now);

        var result = QuotaRiskPolicy.Evaluate(snapshot, null, now);

        Assert.Equal(QuotaRisk.Error, result.Risk);
        Assert.Null(result.DisplayPercent);
        Assert.Equal("no-limits", result.Error?.Code);
        Assert.False(result.IsStale);
    }
}
