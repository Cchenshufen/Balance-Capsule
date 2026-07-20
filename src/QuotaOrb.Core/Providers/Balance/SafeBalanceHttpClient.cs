using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QuotaOrb.Core.Providers.Balance;

public sealed class SafeBalanceHttpClient : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    public const int MaxResponseBytes = 256 * 1024;

    private readonly HttpClient _httpClient;

    public SafeBalanceHttpClient()
        : this(
            new SocketsHttpHandler { AllowAutoRedirect = false },
            DefaultTimeout)
    {
    }

    public SafeBalanceHttpClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = effectiveTimeout
        };
    }

    public async Task<JsonDocument> GetJsonAsync(
        Uri providerBaseUri,
        Uri requestUri,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        ValidateEndpoint(providerBaseUri, "invalid-provider-uri");
        ValidateEndpoint(requestUri, "insecure-request-uri");

        if (!string.Equals(
                providerBaseUri.IdnHost,
                requestUri.IdnHost,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Error("cross-host", "Balance requests must use the provider host.");
        }

        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            throw Error("missing-credential", "A balance credential is required.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            try
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            }
            catch (FormatException)
            {
                throw Error("invalid-credential", "The balance credential is invalid.");
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                throw Error("redirect-not-allowed", "Balance request redirects are not allowed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Error(
                    $"http-{(int)response.StatusCode}",
                    $"Balance request failed with HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw Error("response-too-large", "Balance response exceeded the size limit.");
            }

            await using var body = await ReadLimitedAsync(response.Content, cancellationToken);
            try
            {
                return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                throw Error("invalid-json", "Balance service returned invalid JSON.");
            }
        }
        catch (QuotaProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error("timeout", "Balance request timed out.");
        }
        catch (HttpRequestException)
        {
            throw Error("request-failed", "Balance request failed.");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static void ValidateEndpoint(Uri uri, string code)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.IdnHost) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Error(code, "Balance requests require an absolute HTTPS URI without user information.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var value = (int)statusCode;
        return value is >= 300 and < 400;
    }

    private static async Task<MemoryStream> ReadLimitedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];

        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    destination.Position = 0;
                    return destination;
                }

                if (destination.Length + read > MaxResponseBytes)
                {
                    throw Error("response-too-large", "Balance response exceeded the size limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }

    private static QuotaProviderException Error(string code, string message) =>
        new(code, message);
}
