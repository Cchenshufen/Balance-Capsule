using System.Globalization;
using System.Text.Json;
using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers.Balance;

public sealed class OpenRouterBalanceProvider(
    SafeBalanceHttpClient httpClient,
    TimeProvider? timeProvider = null) : IBalanceProvider
{
    private static readonly Uri Endpoint = new("https://openrouter.ai/api/v1/key");
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<BalanceSnapshot> ReadAsync(
        Uri baseUri,
        string credential,
        CancellationToken cancellationToken)
    {
        using var json = await httpClient.GetJsonAsync(
            baseUri,
            Endpoint,
            credential,
            cancellationToken);

        if (!json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("limit_remaining", out var remaining))
        {
            throw InvalidResponse();
        }

        IReadOnlyList<BalanceAmount> amounts;
        string? note = null;

        if (remaining.ValueKind == JsonValueKind.Null)
        {
            amounts = Array.Empty<BalanceAmount>();
            note = "API key has no configured limit.";
        }
        else if (TryReadDecimal(remaining, out var amount))
        {
            amounts = new[] { new BalanceAmount(amount, "USD") };
        }
        else
        {
            throw InvalidResponse();
        }

        return new BalanceSnapshot(
            "openrouter",
            "OpenRouter",
            BalanceKind.ApiKeyLimitRemaining,
            amounts,
            _timeProvider.GetUtcNow(),
            note);
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out value);
        }

        value = default;
        return element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static QuotaProviderException InvalidResponse() =>
        new("invalid-response", "OpenRouter returned an invalid key limit response.");
}
