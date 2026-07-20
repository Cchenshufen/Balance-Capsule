using QuotaOrb.Core.Providers.Codex;

namespace QuotaOrb.Core.Tests.Providers.Codex;

public sealed class LiveCodexSmokeTests
{
    [Fact]
    public async Task ReadAsync_WithOfficialLocalCli_ReturnsValidWindow()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("QUOTA_ORB_LIVE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var provider = CodexQuotaProvider.CreateDefault();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        var snapshot = await provider.ReadAsync(timeout.Token);

        Assert.True(snapshot.Current is not null || snapshot.Weekly is not null);
        if (snapshot.Current is not null)
        {
            Assert.InRange(snapshot.Current.RemainingPercent, 0, 100);
        }

        if (snapshot.Weekly is not null)
        {
            Assert.InRange(snapshot.Weekly.RemainingPercent, 0, 100);
        }
    }
}
