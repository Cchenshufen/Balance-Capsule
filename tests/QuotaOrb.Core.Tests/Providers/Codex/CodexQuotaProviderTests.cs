using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Codex;

namespace QuotaOrb.Core.Tests.Providers.Codex;

public sealed class CodexQuotaProviderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");

    [Fact]
    public async Task ReadAsync_MapsPrimaryAndSecondaryWindows()
    {
        var transport = CreateTransport(
            "{\"primary\":{\"usedPercent\":14,\"windowDurationMins\":300,\"resetsAt\":1783934400},\"secondary\":{\"usedPercent\":38,\"windowDurationMins\":10080,\"resetsAt\":1784366400}}");
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(86, result.Current?.RemainingPercent);
        Assert.Equal(62, result.Weekly?.RemainingPercent);
        Assert.Equal(TimeSpan.FromMinutes(300), result.Current?.Duration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1784366400), result.Weekly?.ResetsAt);
        Assert.Equal(Now, result.FetchedAt);
    }

    [Fact]
    public async Task ReadAsync_WithPrimaryOnly_LeavesWeeklyNull()
    {
        var transport = CreateTransport(
            "{\"primary\":{\"usedPercent\":27,\"windowDurationMins\":300,\"resetsAt\":1783934400},\"secondary\":null}");
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.NotNull(result.Current);
        Assert.Null(result.Weekly);
    }

    [Fact]
    public async Task ReadAsync_ClampsUsedPercent()
    {
        var transport = CreateTransport(
            "{\"primary\":{\"usedPercent\":-10,\"windowDurationMins\":300,\"resetsAt\":1783934400},\"secondary\":{\"usedPercent\":140,\"windowDurationMins\":10080,\"resetsAt\":1784366400}}");
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(0, result.Current?.UsedPercent);
        Assert.Equal(100, result.Weekly?.UsedPercent);
    }

    [Fact]
    public async Task ReadAsync_WithNoWindows_ThrowsNoLimits()
    {
        var transport = CreateTransport("{\"primary\":null,\"secondary\":null}");
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            provider.ReadAsync(CancellationToken.None));

        Assert.Equal("no-limits", error.Code);
    }

    [Fact]
    public async Task ReadAsync_WithWeeklyPrimary_DoesNotInventFiveHourWindow()
    {
        var transport = CreateTransport(
            "{\"primary\":{\"usedPercent\":93,\"windowDurationMins\":10080,\"resetsAt\":1784366400},\"secondary\":null}");
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Null(result.Current);
        Assert.Equal(7, result.Weekly?.RemainingPercent);
    }

    [Fact]
    public async Task ReadAsync_MapsOfficialAccountTokenBuckets()
    {
        var transport = new ScriptedTransport(new[]
        {
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":93,\"windowDurationMins\":10080,\"resetsAt\":1784366400},\"secondary\":null}}}",
            "{\"id\":3,\"result\":{\"summary\":{\"lifetimeTokens\":758444908},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-01\",\"tokens\":20000000},{\"startDate\":\"2026-07-13\",\"tokens\":248300}]}}"
        });
        var provider = new CodexQuotaProvider(
            () => transport,
            new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(248300, result.TokenUsage?.TodayTokens);
        Assert.Equal(20248300, result.TokenUsage?.MonthTokens);
        Assert.Equal(758444908, result.TokenUsage?.TotalTokens);
    }

    private static ScriptedTransport CreateTransport(string rateLimits)
    {
        return new ScriptedTransport(new[]
        {
            "{\"id\":1,\"result\":{}}",
            $"{{\"id\":2,\"result\":{{\"rateLimits\":{rateLimits}}}}}"
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
