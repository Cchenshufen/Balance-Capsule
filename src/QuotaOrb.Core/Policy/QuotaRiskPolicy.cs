using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Policy;

public static class QuotaRiskPolicy
{
    public static QuotaViewState Evaluate(
        QuotaSnapshot? snapshot,
        QuotaReadError? error,
        DateTimeOffset now)
    {
        if (snapshot is null)
        {
            return error is null
                ? QuotaViewState.Loading(now)
                : new QuotaViewState(
                    null,
                    QuotaRisk.Error,
                    null,
                    null,
                    now,
                    error);
        }

        var valid = new[] { snapshot.Current, snapshot.Weekly }
            .Where(window => window is not null)
            .Cast<QuotaWindow>()
            .Select(window => window.RemainingPercent)
            .ToArray();

        if (valid.Length == 0)
        {
            return new QuotaViewState(
                null,
                QuotaRisk.Error,
                null,
                null,
                now,
                error ?? new QuotaReadError(
                    "no-limits",
                    "Codex returned no rate-limit windows."));
        }

        var display = (int)Math.Round(valid.Min(), MidpointRounding.AwayFromZero);
        var risk = display > 40
            ? QuotaRisk.Safe
            : display >= 20
                ? QuotaRisk.Warning
                : QuotaRisk.Critical;

        return new QuotaViewState(
            display,
            risk,
            snapshot.Current,
            snapshot.Weekly,
            snapshot.FetchedAt,
            error)
        {
            IsStale = error is not null,
            TokenUsage = snapshot.TokenUsage
        };
    }
}
