using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Refresh;

namespace QuotaOrb.Core.Tests.Refresh;

public sealed class QuotaStateServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");

    [Fact]
    public void DefaultRefreshInterval_IsOneMinute() =>
        Assert.Equal(TimeSpan.FromMinutes(1), QuotaStateService.DefaultRefreshInterval);

    [Fact]
    public async Task RefreshNowAsync_SkipsOverlappingRead()
    {
        var snapshot = CreateSnapshot(14, 38);
        var provider = new BlockingProvider(snapshot);
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));
        var published = new List<QuotaViewState>();
        service.StateChanged += (_, state) => published.Add(state);

        var first = service.RefreshNowAsync();
        await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.RefreshNowAsync();

        Assert.Equal(1, provider.ReadCount);
        Assert.Equal(1, provider.MaxConcurrentReads);

        provider.CompleteRead();
        await first;

        var success = Assert.Single(published);
        Assert.Equal(62, success.DisplayPercent);
        Assert.Equal(QuotaRisk.Safe, success.Risk);
    }

    [Fact]
    public async Task RefreshNowAsync_PreservesStaleQuotaOnErrorAndRecovers()
    {
        var initial = CreateSnapshot(14, 38);
        var recovered = CreateSnapshot(10, 20);
        var provider = new SequenceProvider(
            () => Task.FromResult(initial),
            () => Task.FromException<QuotaSnapshot>(
                new QuotaProviderException("timeout", "Codex request timed out.")),
            () => Task.FromResult(recovered));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
        Assert.Equal(62, service.Current.DisplayPercent);
        Assert.Equal("timeout", service.Current.Error?.Code);
        Assert.Equal(initial.Current, service.Current.Current);
        Assert.Equal(initial.Weekly, service.Current.Weekly);
        Assert.Equal(initial.FetchedAt, service.Current.UpdatedAt);
        Assert.True(service.Current.IsStale);

        await service.RefreshNowAsync();

        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
        Assert.Equal(80, service.Current.DisplayPercent);
        Assert.Null(service.Current.Error);
        Assert.False(service.Current.IsStale);
    }

    [Fact]
    public async Task RefreshNowAsync_FirstFailureShowsErrorWithoutStaleQuota()
    {
        var provider = new SequenceProvider(
            () => Task.FromException<QuotaSnapshot>(
                new QuotaProviderException("timeout", "Codex request timed out.")));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));

        await service.RefreshNowAsync();

        Assert.Equal(QuotaRisk.Error, service.Current.Risk);
        Assert.Null(service.Current.DisplayPercent);
        Assert.Equal("timeout", service.Current.Error?.Code);
        Assert.False(service.Current.IsStale);
    }

    [Fact]
    public async Task RefreshNowAsync_StatusUnavailableShowsNeutralNotice()
    {
        var provider = new SequenceProvider(
            () => Task.FromException<QuotaSnapshot>(
                new QuotaProviderException("status-unavailable", "CLI 尚未同步。")));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));

        await service.RefreshNowAsync();

        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
        Assert.Equal("CLI 尚未同步。", service.Current.UnsupportedReason);
        Assert.Null(service.Current.Error);
    }

    [Fact]
    public async Task RefreshNowAsync_EmptyRefreshDoesNotReplaceLastSuccessfulQuota()
    {
        var initial = CreateSnapshot(14, 38);
        var provider = new SequenceProvider(
            () => Task.FromResult(initial),
            () => Task.FromResult(new QuotaSnapshot(null, null, Now.AddMinutes(1))));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now.AddMinutes(1)));

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
        Assert.Equal(62, service.Current.DisplayPercent);
        Assert.Equal("no-limits", service.Current.Error?.Code);
        Assert.Equal(initial.FetchedAt, service.Current.UpdatedAt);
        Assert.True(service.Current.IsStale);
    }

    [Fact]
    public async Task RefreshNowAsync_MapsUnexpectedExceptionWithoutStackTrace()
    {
        var provider = new SequenceProvider(
            () => Task.FromException<QuotaSnapshot>(new InvalidOperationException("secret details")));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));

        await service.RefreshNowAsync();

        Assert.Equal("unexpected", service.Current.Error?.Code);
        Assert.Equal("Unexpected error while reading Codex quota.", service.Current.Error?.Message);
        Assert.DoesNotContain("QuotaStateServiceTests", service.Current.Error?.Message);
        Assert.False(service.Current.IsStale);
    }

    [Fact]
    public async Task StartAndStop_WhileImmediateReadIsBlocked_CompletesCleanly()
    {
        var provider = new BlockingProvider(CreateSnapshot(14, 38));
        await using var service = new QuotaStateService(
            provider,
            new FixedTimeProvider(Now));

        var start = service.StartAsync();
        await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await service.StopAsync();
        await start;

        Assert.Equal(1, provider.ReadCount);
    }

    private static QuotaSnapshot CreateSnapshot(double currentUsed, double weeklyUsed)
    {
        return new QuotaSnapshot(
            new QuotaWindow(currentUsed, TimeSpan.FromHours(5), Now.AddHours(4)),
            new QuotaWindow(weeklyUsed, TimeSpan.FromDays(7), Now.AddDays(5)),
            Now);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class BlockingProvider(QuotaSnapshot snapshot) : IQuotaProvider
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrentReads;

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReadCount { get; private set; }

        public int MaxConcurrentReads { get; private set; }

        public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            var concurrent = Interlocked.Increment(ref _concurrentReads);
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, concurrent);
            ReadStarted.TrySetResult();

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return snapshot;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentReads);
            }
        }

        public void CompleteRead() => _release.TrySetResult();
    }

    private sealed class SequenceProvider(params Func<Task<QuotaSnapshot>>[] reads) : IQuotaProvider
    {
        private readonly Queue<Func<Task<QuotaSnapshot>>> _reads = new(reads);

        public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _reads.Dequeue()();
        }
    }
}
