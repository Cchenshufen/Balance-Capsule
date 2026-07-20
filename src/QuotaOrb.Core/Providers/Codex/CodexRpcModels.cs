namespace QuotaOrb.Core.Providers.Codex;

public sealed record RpcRateLimitsResponse(RpcRateLimits RateLimits);

public sealed record RpcRateLimits(
    RpcRateLimitWindow? Primary,
    RpcRateLimitWindow? Secondary);

public sealed record RpcRateLimitWindow(
    double UsedPercent,
    int? WindowDurationMins,
    long? ResetsAt);

public sealed record RpcAccountUsageResponse(
    RpcAccountUsageSummary? Summary,
    IReadOnlyList<RpcDailyUsageBucket>? DailyUsageBuckets);

public sealed record RpcAccountUsageSummary(long? LifetimeTokens);

public sealed record RpcDailyUsageBucket(string StartDate, long Tokens);

public sealed class CodexRpcException : Exception
{
    public CodexRpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }
}
