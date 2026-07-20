using System.Text.Json;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Claude;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Providers.Claude;

public sealed class ClaudeStatusQuotaProviderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-15T12:00:00+08:00");

    [Fact]
    public async Task ReadAsync_MapsFiveHourAndSevenDayRateLimits()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory(".");
        var path = Path.Combine(directory.Root, "claude-status.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new ClaudeStatusQuotaProvider.ClaudeStatusCache(
                Now.AddMinutes(-1),
                new ClaudeStatusQuotaProvider.ClaudeStatusWindow(23.5, Now.AddHours(4)),
                new ClaudeStatusQuotaProvider.ClaudeStatusWindow(41.2, Now.AddDays(5)))));
        var provider = new ClaudeStatusQuotaProvider(path, new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(CancellationToken.None);

        Assert.Equal(76.5, result.Current?.RemainingPercent);
        Assert.Equal(58.8, result.Weekly?.RemainingPercent);
        Assert.Equal(TimeSpan.FromHours(5), result.Current?.Duration);
        Assert.Equal(TimeSpan.FromDays(7), result.Weekly?.Duration);
    }

    [Fact]
    public async Task ReadAsync_WithExpiredWindows_ThrowsStaleStatus()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateDirectory(".");
        var path = Path.Combine(directory.Root, "claude-status.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new ClaudeStatusQuotaProvider.ClaudeStatusCache(
                Now.AddDays(-8),
                new ClaudeStatusQuotaProvider.ClaudeStatusWindow(99, Now.AddMinutes(-1)),
                null)));
        var provider = new ClaudeStatusQuotaProvider(path, new FixedTimeProvider(Now));

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            provider.ReadAsync(CancellationToken.None));

        Assert.Equal("stale-status", error.Code);
    }

    [Fact]
    public async Task ReadAsync_WithoutCache_ExplainsDesktopAndCliBoundary()
    {
        using var directory = new TemporaryDirectory();
        var provider = new ClaudeStatusQuotaProvider(
            Path.Combine(directory.Root, "missing.json"),
            new FixedTimeProvider(Now));

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            provider.ReadAsync(CancellationToken.None));

        Assert.Equal("status-unavailable", error.Code);
        Assert.Contains("Claude Desktop", error.Message);
        Assert.Contains("Claude Code CLI", error.Message);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
