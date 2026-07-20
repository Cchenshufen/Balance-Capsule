namespace QuotaOrb.Core.Domain;

public sealed record QuotaWindow(double UsedPercent, TimeSpan? Duration, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}
