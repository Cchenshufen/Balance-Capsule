namespace QuotaOrb.Core.Domain;

public enum BalanceKind
{
    AccountBalance,
    ApiKeyLimitRemaining
}

public sealed record BalanceAmount(decimal Amount, string Currency);

public sealed record BalanceSnapshot(
    string ProviderId,
    string DisplayName,
    BalanceKind Kind,
    IReadOnlyList<BalanceAmount> Amounts,
    DateTimeOffset FetchedAt,
    string? Note = null);
