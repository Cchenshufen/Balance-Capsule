namespace QuotaOrb.Core.Domain;

public sealed record QuotaSnapshot(
    QuotaWindow? Current,
    QuotaWindow? Weekly,
    DateTimeOffset FetchedAt)
{
    public TokenUsageSummary? TokenUsage { get; init; }
}
