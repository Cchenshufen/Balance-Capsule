using System.Globalization;
using System.Text.Json;
using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers.Balance;

public sealed class DeepSeekBalanceProvider(
    SafeBalanceHttpClient httpClient,
    TimeProvider? timeProvider = null) : IBalanceProvider
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<BalanceSnapshot> ReadAsync(
        Uri baseUri,
        string credential,
        CancellationToken cancellationToken)
    {
        using var json = await httpClient.GetJsonAsync(
            baseUri,
            new Uri(baseUri, "/user/balance"),
            credential,
            cancellationToken);

        if (!json.RootElement.TryGetProperty("balance_infos", out var infos) ||
            infos.ValueKind != JsonValueKind.Array)
        {
            throw InvalidResponse();
        }

        var amounts = new List<BalanceAmount>();
        foreach (var info in infos.EnumerateArray())
        {
            if (info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("currency", out var currencyElement) ||
                currencyElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(currencyElement.GetString()) ||
                !info.TryGetProperty("total_balance", out var amountElement) ||
                !TryReadDecimal(amountElement, out var amount))
            {
                throw InvalidResponse();
            }

            amounts.Add(new BalanceAmount(amount, currencyElement.GetString()!));
        }

        var unavailable = json.RootElement.TryGetProperty("is_available", out var available) &&
            available.ValueKind is JsonValueKind.False;

        return new BalanceSnapshot(
            "deepseek",
            "DeepSeek",
            BalanceKind.AccountBalance,
            amounts,
            _timeProvider.GetUtcNow(),
            unavailable ? "Account balance is unavailable." : null);
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
        new("invalid-response", "DeepSeek returned an invalid balance response.");
}
