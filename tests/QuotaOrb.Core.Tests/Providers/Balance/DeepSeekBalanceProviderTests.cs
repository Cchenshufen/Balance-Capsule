using System.Net;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers.Balance;

namespace QuotaOrb.Core.Tests.Providers.Balance;

public sealed class DeepSeekBalanceProviderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-14T12:00:00+08:00");

    [Fact]
    public async Task ReadAsync_ParsesEveryCurrencyWithoutConversion()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"is_available\":true,\"balance_infos\":[" +
                    "{\"currency\":\"CNY\",\"total_balance\":\"28.50\"}," +
                    "{\"currency\":\"USD\",\"total_balance\":\"4.25\"}]}")
            });
        });
        using var client = new SafeBalanceHttpClient(handler);
        var provider = new DeepSeekBalanceProvider(client, new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(
            new Uri("https://api.deepseek.com/v1"),
            "test-token",
            CancellationToken.None);

        Assert.Equal("https://api.deepseek.com/user/balance", requestedUri?.AbsoluteUri);
        Assert.Equal("deepseek", result.ProviderId);
        Assert.Equal("DeepSeek", result.DisplayName);
        Assert.Equal(BalanceKind.AccountBalance, result.Kind);
        Assert.Equal(Now, result.FetchedAt);
        Assert.Null(result.Note);
        Assert.Collection(
            result.Amounts,
            item => Assert.Equal(new BalanceAmount(28.50m, "CNY"), item),
            item => Assert.Equal(new BalanceAmount(4.25m, "USD"), item));
    }

    [Fact]
    public async Task ReadAsync_WithUnavailableAccount_ReturnsExplanation()
    {
        var handler = JsonHandler("{\"is_available\":false,\"balance_infos\":[]}");
        using var client = new SafeBalanceHttpClient(handler);
        var provider = new DeepSeekBalanceProvider(client, new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(
            new Uri("https://api.deepseek.com"),
            "test-token",
            CancellationToken.None);

        Assert.Empty(result.Amounts);
        Assert.Equal("Account balance is unavailable.", result.Note);
    }

    private static StubHttpMessageHandler JsonHandler(string json) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        }));
}
