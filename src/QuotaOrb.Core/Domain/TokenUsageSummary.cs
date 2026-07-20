namespace QuotaOrb.Core.Domain;

public sealed record TokenUsageSummary(
    long TodayTokens,
    long MonthTokens,
    long TotalTokens);
