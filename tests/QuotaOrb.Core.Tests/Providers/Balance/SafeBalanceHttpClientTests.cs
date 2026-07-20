using System.Net;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Balance;

namespace QuotaOrb.Core.Tests.Providers.Balance;

public sealed class SafeBalanceHttpClientTests
{
    private static readonly Uri ProviderUri = new("https://api.example.com/v1");

    [Fact]
    public async Task GetJsonAsync_SendsOnlyGetWithBearerCredential()
    {
        HttpMethod? method = null;
        string? scheme = null;
        string? credential = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            method = request.Method;
            scheme = request.Headers.Authorization?.Scheme;
            credential = request.Headers.Authorization?.Parameter;
            return Task.FromResult(JsonResponse("{\"ok\":true}"));
        });
        using var client = new SafeBalanceHttpClient(handler);

        using var json = await client.GetJsonAsync(
            ProviderUri,
            new Uri("https://api.example.com/balance"),
            "test-token",
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("Bearer", scheme);
        Assert.Equal("test-token", credential);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task GetJsonAsync_RejectsHttpBeforeSending()
    {
        var handler = NeverCalledHandler();
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("http://api.example.com/balance"),
                "test-token",
                CancellationToken.None));

        Assert.Equal("insecure-request-uri", error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetJsonAsync_RejectsCrossHostBeforeSending()
    {
        var handler = NeverCalledHandler();
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://other.example.com/balance"),
                "test-token",
                CancellationToken.None));

        Assert.Equal("cross-host", error.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task GetJsonAsync_RejectsRedirect()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://api.example.com/elsewhere") }
            }));
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://api.example.com/balance"),
                "test-token",
                CancellationToken.None));

        Assert.Equal("redirect-not-allowed", error.Code);
    }

    [Fact]
    public async Task GetJsonAsync_TimesOutWithoutExposingCredential()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        using var client = new SafeBalanceHttpClient(handler, TimeSpan.FromMilliseconds(20));

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://api.example.com/balance"),
                "secret-test-token",
                CancellationToken.None));

        Assert.Equal("timeout", error.Code);
        Assert.DoesNotContain("secret-test-token", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJsonAsync_RejectsOversizedResponse()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[SafeBalanceHttpClient.MaxResponseBytes + 1])
            }));
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://api.example.com/balance"),
                "test-token",
                CancellationToken.None));

        Assert.Equal("response-too-large", error.Code);
    }

    [Fact]
    public async Task GetJsonAsync_RedactsErrorResponseAndCredential()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("raw-sensitive-response")
            }));
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://api.example.com/balance"),
                "secret-test-token",
                CancellationToken.None));

        Assert.Equal("http-401", error.Code);
        Assert.DoesNotContain("secret-test-token", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-sensitive-response", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJsonAsync_RedactsMalformedCredential()
    {
        var handler = NeverCalledHandler();
        using var client = new SafeBalanceHttpClient(handler);

        var error = await Assert.ThrowsAsync<QuotaProviderException>(() =>
            client.GetJsonAsync(
                ProviderUri,
                new Uri("https://api.example.com/balance"),
                "secret\r\nheader",
                CancellationToken.None));

        Assert.Equal("invalid-credential", error.Code);
        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, handler.CallCount);
    }

    private static StubHttpMessageHandler NeverCalledHandler() =>
        new((_, _) => throw new InvalidOperationException("The request should not be sent."));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };
}
