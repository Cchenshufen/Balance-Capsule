using System.Security.Cryptography;
using System.Text;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Policy;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Balance;
using QuotaOrb.Core.Providers.Detection;

namespace QuotaOrb.Core.Refresh;

public sealed class QuotaStateService : IAsyncDisposable
{
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DefaultDetectionInterval = TimeSpan.FromSeconds(5);

    private readonly IQuotaProvider _provider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _detectionInterval;
    private readonly TimeSpan _balanceRefreshInterval;
    private readonly IActiveProviderDetector? _activeProviderDetector;
    private readonly IReadOnlyDictionary<string, IBalanceProvider> _balanceProviders;
    private readonly string _officialSourceName = "Codex 官方";
    private readonly byte[]? _credentialFingerprintKey;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private QuotaSnapshot? _lastSnapshot;
    private BalanceSnapshot? _lastBalance;
    private ActiveProvider? _activeProvider;
    private DateTimeOffset? _lastBalanceAttemptAt;
    private DateTimeOffset? _lastOfficialAttemptAt;
    private int _followUpRefreshesRemaining;
    private byte[]? _lastCredentialFingerprint;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _disposed;

    public QuotaStateService(
        IQuotaProvider provider,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
        _detectionInterval = _refreshInterval;
        _balanceRefreshInterval = TimeSpan.FromMinutes(1);
        _balanceProviders = new Dictionary<string, IBalanceProvider>();

        if (_refreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "The refresh interval must be positive.");
        }

        Current = QuotaViewState.Loading(_timeProvider.GetUtcNow());
    }

    public QuotaStateService(
        IQuotaProvider provider,
        IActiveProviderDetector activeProviderDetector,
        IReadOnlyDictionary<string, IBalanceProvider> balanceProviders,
        TimeProvider? timeProvider = null,
        TimeSpan? refreshInterval = null,
        TimeSpan? balanceRefreshInterval = null,
        string officialSourceName = "Codex 官方",
        TimeSpan? detectionInterval = null)
        : this(provider, timeProvider, refreshInterval)
    {
        _activeProviderDetector = activeProviderDetector ??
            throw new ArgumentNullException(nameof(activeProviderDetector));
        _balanceProviders = balanceProviders ??
            throw new ArgumentNullException(nameof(balanceProviders));
        _officialSourceName = string.IsNullOrWhiteSpace(officialSourceName)
            ? throw new ArgumentException("Official source name is required.", nameof(officialSourceName))
            : officialSourceName;
        Current = Current with { SourceName = _officialSourceName };
        _credentialFingerprintKey = RandomNumberGenerator.GetBytes(32);
        _balanceRefreshInterval = balanceRefreshInterval ?? TimeSpan.FromMinutes(1);
        _detectionInterval = detectionInterval ?? DefaultDetectionInterval;

        if (_balanceRefreshInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(balanceRefreshInterval),
                "The balance refresh interval must be positive.");
        }

        if (_detectionInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionInterval),
                "The detection interval must be positive.");
        }
    }

    public QuotaViewState Current { get; private set; }

    public event EventHandler<QuotaViewState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        CancellationTokenSource loopCancellation;
        lock (_lifecycleGate)
        {
            if (_loopCancellation is not null)
            {
                return;
            }

            loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loopCancellation = loopCancellation;
        }
        var loopToken = loopCancellation.Token;

        Publish(QuotaViewState.Loading(_timeProvider.GetUtcNow()));

        try
        {
            await RefreshNowAsync(loopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            loopToken.IsCancellationRequested)
        {
            return;
        }

        lock (_lifecycleGate)
        {
            if (!ReferenceEquals(_loopCancellation, loopCancellation) ||
                loopToken.IsCancellationRequested)
            {
                return;
            }

            _loopTask = RunPeriodicLoopAsync(loopToken);
        }
    }

    public Task RefreshNowAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(forceRefresh: true, cancellationToken);

    private async Task RefreshAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var followUpRefresh = false;
        try
        {
            var now = _timeProvider.GetUtcNow();
            if (_activeProviderDetector is null)
            {
                await RefreshOfficialAsync(null, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            var detected = await _activeProviderDetector
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
            var sourceChanged = !string.Equals(
                _activeProvider?.Fingerprint,
                detected.Fingerprint,
                StringComparison.Ordinal);
            if (sourceChanged)
            {
                ResetCachedSource();
                _followUpRefreshesRemaining = 1;
            }

            followUpRefresh = !sourceChanged && _followUpRefreshesRemaining > 0;

            _activeProvider = detected;

            if (detected.Mode == ActiveProviderMode.Official)
            {
                if (!forceRefresh &&
                    !sourceChanged &&
                    !followUpRefresh &&
                    _lastOfficialAttemptAt is { } officialAttemptedAt &&
                    now - officialAttemptedAt < _refreshInterval)
                {
                    return;
                }

                _lastOfficialAttemptAt = now;
                await RefreshOfficialAsync(detected, now, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!detected.IsSupported ||
                detected.BaseUri is null ||
                string.IsNullOrWhiteSpace(detected.BalanceTemplate))
            {
                PublishUnsupported(detected, now);
                return;
            }

            if (!forceRefresh &&
                !sourceChanged &&
                !followUpRefresh &&
                _lastBalanceAttemptAt is { } attemptedAt &&
                now - attemptedAt < _balanceRefreshInterval)
            {
                return;
            }

            await RefreshBalanceAsync(detected, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (QuotaProviderException exception)
        {
            if (exception.Code == "status-unavailable")
            {
                PublishUnavailable(exception.Message, _timeProvider.GetUtcNow());
                return;
            }

            var error = new QuotaReadError(exception.Code, exception.Message);
            PublishReadFailure(error, _timeProvider.GetUtcNow());
        }
        catch (Exception)
        {
            var error = new QuotaReadError(
                "unexpected",
                _activeProvider is null or { Mode: ActiveProviderMode.Official }
                    ? "Unexpected error while reading Codex quota."
                    : "Unexpected error while reading provider balance.");
            PublishReadFailure(error, _timeProvider.GetUtcNow());
        }
        finally
        {
            if (followUpRefresh)
            {
                _followUpRefreshesRemaining = Math.Max(0, _followUpRefreshesRemaining - 1);
            }
            _refreshGate.Release();
        }
    }

    private async Task RefreshOfficialAsync(
        ActiveProvider? provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var snapshot = await _provider.ReadAsync(cancellationToken).ConfigureAwait(false);
        var state = QuotaRiskPolicy.Evaluate(snapshot, null, now);

        if (state.Risk != QuotaRisk.Error)
        {
            _lastSnapshot = snapshot;
        }
        else if (_lastSnapshot is not null && state.Error is not null)
        {
            state = QuotaRiskPolicy.Evaluate(_lastSnapshot, state.Error, now);
        }

        Publish(state with { SourceName = FormatSourceName(provider) });
    }

    private async Task RefreshBalanceAsync(
        ActiveProvider provider,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_balanceProviders.TryGetValue(provider.BalanceTemplate!, out var balanceProvider))
        {
            PublishUnsupported(
                provider with { SupportReason = "当前版本没有对应的余额适配器。" },
                now);
            return;
        }

        string? credential = await _activeProviderDetector!
            .ReadCredentialAsync(provider, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credential))
        {
            if (!provider.IsPinnedToRunningAgent)
            {
                ResetBalanceCredential();
            }

            _lastBalanceAttemptAt = now;
            throw new QuotaProviderException(
                "missing-credential",
                provider.IsPinnedToRunningAgent
                    ? "第三方工具已退出；保留运行中 Agent 的上次余额，重启 Agent 后重新检测来源。"
                    : "无法读取当前供应商的余额凭据。");
        }

        var credentialFingerprint = FingerprintCredential(credential);
        if (_lastCredentialFingerprint is not null &&
            !CryptographicOperations.FixedTimeEquals(
                _lastCredentialFingerprint,
                credentialFingerprint))
        {
            _lastBalance = null;
            _lastBalanceAttemptAt = null;
        }

        ReplaceCredentialFingerprint(credentialFingerprint);
        _lastBalanceAttemptAt = now;

        BalanceSnapshot snapshot;
        try
        {
            snapshot = await balanceProvider
                .ReadAsync(provider.BaseUri!, credential, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            credential = null;
        }

        _lastBalance = snapshot;
        Publish(new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            snapshot.FetchedAt,
            null)
        {
            Balance = snapshot,
            SourceName = FormatSourceName(provider)
        });
    }

    private void PublishReadFailure(QuotaReadError error, DateTimeOffset now)
    {
        if (_activeProvider is not null &&
            _activeProvider.Mode != ActiveProviderMode.Official)
        {
            Publish(new QuotaViewState(
                null,
                _lastBalance is null ? QuotaRisk.Error : QuotaRisk.Safe,
                null,
                null,
                _lastBalance?.FetchedAt ?? now,
                error)
            {
                Balance = _lastBalance,
                SourceName = FormatSourceName(_activeProvider),
                IsStale = _lastBalance is not null
            });
            return;
        }

        Publish(QuotaRiskPolicy.Evaluate(_lastSnapshot, error, now) with
        {
            SourceName = FormatSourceName(_activeProvider)
        });
    }

    private void PublishUnsupported(ActiveProvider provider, DateTimeOffset now)
    {
        Publish(new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            now,
            null)
        {
            SourceName = FormatSourceName(provider),
            UnsupportedReason = provider.SupportReason ?? "当前来源暂不支持余额查询。"
        });
    }

    private void PublishUnavailable(string reason, DateTimeOffset now)
    {
        Publish(new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            now,
            null)
        {
            SourceName = FormatSourceName(_activeProvider),
            UnsupportedReason = reason
        });
    }

    private void ResetCachedSource()
    {
        _lastSnapshot = null;
        _lastBalance = null;
        _lastBalanceAttemptAt = null;
        _lastOfficialAttemptAt = null;
        ClearCredentialFingerprint();
    }

    private byte[] FingerprintCredential(string credential)
    {
        var credentialBytes = Encoding.UTF8.GetBytes(credential);
        try
        {
            return HMACSHA256.HashData(_credentialFingerprintKey!, credentialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
        }
    }

    private void ResetBalanceCredential()
    {
        _lastBalance = null;
        _lastBalanceAttemptAt = null;
        ClearCredentialFingerprint();
    }

    private void ReplaceCredentialFingerprint(byte[] fingerprint)
    {
        ClearCredentialFingerprint();
        _lastCredentialFingerprint = fingerprint;
    }

    private void ClearCredentialFingerprint()
    {
        if (_lastCredentialFingerprint is not null)
        {
            CryptographicOperations.ZeroMemory(_lastCredentialFingerprint);
            _lastCredentialFingerprint = null;
        }
    }

    private string FormatSourceName(ActiveProvider? provider) => provider?.Mode switch
    {
        ActiveProviderMode.CcSwitch => $"CC Switch · {provider.DisplayName}",
        ActiveProviderMode.CodexPlusPlus => $"Codex++ · {provider.DisplayName}",
        ActiveProviderMode.Direct => provider.DisplayName,
        _ => _officialSourceName
    };

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? loop;

        lock (_lifecycleGate)
        {
            cancellation = _loopCancellation;
            loop = _loopTask;
            _loopCancellation = null;
            _loopTask = null;
        }

        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Cancellation is the normal way to stop the periodic loop.
            }
        }

        cancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        ResetCachedSource();
        if (_credentialFingerprintKey is not null)
        {
            CryptographicOperations.ZeroMemory(_credentialFingerprintKey);
        }
        _refreshGate.Dispose();
        _disposed = true;
    }

    private async Task RunPeriodicLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_detectionInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await RefreshAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Publish(QuotaViewState state)
    {
        Current = state;
        StateChanged?.Invoke(this, state);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
