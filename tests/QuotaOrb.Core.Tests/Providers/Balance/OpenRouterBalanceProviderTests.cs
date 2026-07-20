using System.Net;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers.Balance;

namespace QuotaOrb.Core.Tests.Providers.Balance;

public sealed class OpenRouterBalanceProviderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-14T12:00:00+08:00");

    [Fact]
    public async Task ReadAsync_UsesKeyEndpointAndPreservesKeyLimitSemantics()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(JsonResponse("{\"data\":{\"limit_remaining\":12.34}}"));
        });
        using var client = new SafeBalanceHttpClient(handler);
        var provider = new OpenRouterBalanceProvider(client, new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(
            new Uri("https://openrouter.ai/api/v1"),
            "test-token",
            CancellationToken.None);

        Assert.Equal("https://openrouter.ai/api/v1/key", requestedUri?.AbsoluteUri);
        Assert.DoesNotContain("credits", requestedUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("openrouter", result.ProviderId);
        Assert.Equal("OpenRouter", result.DisplayName);
        Assert.Equal(BalanceKind.ApiKeyLimitRemaining, result.Kind);
        Assert.Equal(Now, result.FetchedAt);
        Assert.Null(result.Note);
        Assert.Equal(new BalanceAmount(12.34m, "USD"), Assert.Single(result.Amounts));
    }

    [Fact]
    public async Task ReadAsync_WithNullLimit_ReturnsNoAmountAndExplanation()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{\"data\":{\"limit_remaining\":null}}")));
        using var client = new SafeBalanceHttpClient(handler);
        var provider = new OpenRouterBalanceProvider(client, new FixedTimeProvider(Now));

        var result = await provider.ReadAsync(
            new Uri("https://openrouter.ai/api/v1"),
            "test-token",
            CancellationToken.None);

        Assert.Equal(BalanceKind.ApiKeyLimitRemaining, result.Kind);
        Assert.Empty(result.Amounts);
        Assert.Equal("API key has no configured limit.", result.Note);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
