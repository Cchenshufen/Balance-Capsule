namespace QuotaOrb.Core.Domain;

public sealed record QuotaViewState(
    int? DisplayPercent,
    QuotaRisk Risk,
    QuotaWindow? Current,
    QuotaWindow? Weekly,
    DateTimeOffset UpdatedAt,
    QuotaReadError? Error)
{
    public bool IsStale { get; init; }

    public BalanceSnapshot? Balance { get; init; }

    public TokenUsageSummary? TokenUsage { get; init; }

    public string? SourceName { get; init; }

    public string? UnsupportedReason { get; init; }

    public static QuotaViewState Loading(DateTimeOffset now) =>
        new(null, QuotaRisk.Loading, null, null, now, null);
}
