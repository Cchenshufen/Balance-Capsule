using System.ComponentModel;
using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers.Codex;

public sealed class CodexQuotaProvider : IQuotaProvider
{
    private readonly Func<ICodexRpcTransport> _transportFactory;
    private readonly TimeProvider _timeProvider;

    public CodexQuotaProvider(
        Func<ICodexRpcTransport> transportFactory,
        TimeProvider? timeProvider = null)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static CodexQuotaProvider CreateDefault()
    {
        return new CodexQuotaProvider(() =>
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var command = CodexCommandResolver.Resolve(path, pathExt, localAppData, programFiles);
            return ProcessCodexRpcTransport.Start(command);
        });
    }

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var transport = _transportFactory();
            await using var client = new CodexRpcClient(transport);
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var response = await client.ReadRateLimitsAsync(cancellationToken).ConfigureAwait(false);

            var primary = MapWindow(response.RateLimits.Primary);
            var secondary = MapWindow(response.RateLimits.Secondary);
            var current = MatchWindow(TimeSpan.FromHours(5), primary, secondary);
            var weekly = MatchWindow(TimeSpan.FromDays(7), primary, secondary);
            if (current is null && weekly is null)
            {
                throw new QuotaProviderException(
                    "no-limits",
                    "Codex returned no rate-limit windows.");
            }

            TokenUsageSummary? tokenUsage = null;
            try
            {
                var usage = await client.ReadAccountUsageAsync(cancellationToken).ConfigureAwait(false);
                tokenUsage = MapTokenUsage(usage);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is TimeoutException or CodexRpcException or InvalidDataException or EndOfStreamException)
            {
                tokenUsage = null;
            }

            return new QuotaSnapshot(current, weekly, _timeProvider.GetUtcNow())
            {
                TokenUsage = tokenUsage
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QuotaProviderException)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            throw new QuotaProviderException(
                "cli-not-found",
                "Official Codex desktop runtime or CLI was not found.",
                exception);
        }
        catch (TimeoutException exception)
        {
            throw new QuotaProviderException(
                "timeout",
                "Codex rate-limit request timed out.",
                exception);
        }
        catch (CodexRpcException exception)
        {
            throw new QuotaProviderException(
                "rpc-error",
                exception.Message,
                exception);
        }
        catch (InvalidDataException exception)
        {
            throw new QuotaProviderException(
                "protocol-error",
                "Codex returned an invalid rate-limit response.",
                exception);
        }
        catch (EndOfStreamException exception)
        {
            throw new QuotaProviderException(
                "protocol-error",
                "Codex app-server closed before returning rate limits.",
                exception);
        }
        catch (Win32Exception exception)
        {
            throw new QuotaProviderException(
                "start-failed",
                "Official Codex CLI could not be started.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new QuotaProviderException(
                "start-failed",
                "Official Codex CLI could not be started.",
                exception);
        }
    }

    private static QuotaWindow? MapWindow(RpcRateLimitWindow? window)
    {
        if (window is null)
        {
            return null;
        }

        TimeSpan? duration = window.WindowDurationMins is null
            ? null
            : TimeSpan.FromMinutes(Math.Max(0, window.WindowDurationMins.Value));
        DateTimeOffset? resetsAt = window.ResetsAt is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value);

        return new QuotaWindow(
            Math.Clamp(window.UsedPercent, 0d, 100d),
            duration,
            resetsAt);
    }

    private static QuotaWindow? MatchWindow(
        TimeSpan duration,
        QuotaWindow? first,
        QuotaWindow? second)
    {
        if (first?.Duration is { } firstDuration &&
            Math.Abs((firstDuration - duration).TotalMinutes) < 1d)
        {
            return first;
        }

        if (second?.Duration is { } secondDuration &&
            Math.Abs((secondDuration - duration).TotalMinutes) < 1d)
        {
            return second;
        }

        if (first?.Duration is not null || second?.Duration is not null)
        {
            return null;
        }

        return duration == TimeSpan.FromHours(5) ? first : second;
    }

    private TokenUsageSummary? MapTokenUsage(RpcAccountUsageResponse response)
    {
        if (response.Summary?.LifetimeTokens is not { } lifetimeTokens)
        {
            return null;
        }

        var localToday = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        var todayKey = localToday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var monthKey = localToday.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        long todayTokens = 0;
        long monthTokens = 0;
        foreach (var bucket in response.DailyUsageBuckets ?? Array.Empty<RpcDailyUsageBucket>())
        {
            if (string.Equals(bucket.StartDate, todayKey, StringComparison.Ordinal))
            {
                todayTokens = Math.Max(0, bucket.Tokens);
            }

            if (bucket.StartDate.StartsWith(monthKey, StringComparison.Ordinal))
            {
                monthTokens = AddWithoutOverflow(monthTokens, Math.Max(0, bucket.Tokens));
            }
        }

        return new TokenUsageSummary(
            todayTokens,
            monthTokens,
            Math.Max(0, lifetimeTokens));
    }

    private static long AddWithoutOverflow(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}
