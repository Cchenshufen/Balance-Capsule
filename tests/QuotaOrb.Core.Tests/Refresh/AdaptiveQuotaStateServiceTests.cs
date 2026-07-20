using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Balance;
using QuotaOrb.Core.Providers.Detection;
using QuotaOrb.Core.Refresh;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Refresh;

public sealed class AdaptiveQuotaStateServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-14T12:00:00+08:00");

    [Fact]
    public async Task RefreshNowAsync_DirectProviderPublishesBalanceWithoutCallingOfficialQuota()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(directory, "deepseek", "DeepSeek", "https://api.deepseek.com", "secret");
        var balance = new RecordingBalanceProvider(CreateBalance("deepseek", "DeepSeek", 28m));
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider> { ["deepseek"] = balance });

        await service.RefreshNowAsync();

        Assert.Equal("secret", balance.Credential);
        Assert.Equal("https://api.deepseek.com/", balance.BaseUri?.AbsoluteUri);
        Assert.Equal(28m, Assert.Single(service.Current.Balance!.Amounts).Amount);
        Assert.Equal("DeepSeek", service.Current.SourceName);
        Assert.Null(service.Current.DisplayPercent);
        Assert.Null(service.Current.Error);
    }

    [Fact]
    public async Task RefreshNowAsync_SameBalanceSourceFailureKeepsStaleBalance()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(directory, "deepseek", "DeepSeek", "https://api.deepseek.com", "secret");
        var balance = new SequenceBalanceProvider(
            () => Task.FromResult(CreateBalance("deepseek", "DeepSeek", 28m)),
            () => Task.FromException<BalanceSnapshot>(
                new QuotaProviderException("timeout", "Balance request timed out.")));
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider> { ["deepseek"] = balance });

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(28m, Assert.Single(service.Current.Balance!.Amounts).Amount);
        Assert.Equal("timeout", service.Current.Error?.Code);
        Assert.True(service.Current.IsStale);
        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
    }

    [Fact]
    public async Task RefreshNowAsync_SourceChangeDoesNotLeakPreviousBalance()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(directory, "deepseek", "DeepSeek", "https://api.deepseek.com", "secret");
        var deepSeek = new RecordingBalanceProvider(CreateBalance("deepseek", "DeepSeek", 28m));
        var openRouter = new SequenceBalanceProvider(
            () => Task.FromException<BalanceSnapshot>(
                new QuotaProviderException("timeout", "Balance request timed out.")));
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider>
            {
                ["deepseek"] = deepSeek,
                ["openrouter"] = openRouter
            });

        await service.RefreshNowAsync();
        await File.WriteAllTextAsync(configPath, Config(
            "openrouter",
            "OpenRouter",
            "https://openrouter.ai/api/v1",
            "new-secret"));
        await service.RefreshNowAsync();

        Assert.Null(service.Current.Balance);
        Assert.False(service.Current.IsStale);
        Assert.Equal(QuotaRisk.Error, service.Current.Risk);
        Assert.Equal("OpenRouter", service.Current.SourceName);
    }

    [Fact]
    public async Task RefreshNowAsync_CredentialChangeFailureDoesNotLeakPreviousBalance()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(
            directory,
            "deepseek",
            "DeepSeek",
            "https://api.deepseek.com",
            "old-secret");
        var balance = new SequenceBalanceProvider(
            () => Task.FromResult(CreateBalance("deepseek", "DeepSeek", 28m)),
            () => Task.FromException<BalanceSnapshot>(
                new QuotaProviderException("unauthorized", "Balance request failed with HTTP 401.")));
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider> { ["deepseek"] = balance });

        await service.RefreshNowAsync();
        await File.WriteAllTextAsync(configPath, Config(
            "deepseek",
            "DeepSeek",
            "https://api.deepseek.com",
            "new-secret"));
        await service.RefreshNowAsync();

        Assert.Null(service.Current.Balance);
        Assert.False(service.Current.IsStale);
        Assert.Equal(QuotaRisk.Error, service.Current.Risk);
        Assert.Equal("unauthorized", service.Current.Error?.Code);
    }

    [Fact]
    public async Task RefreshNowAsync_CodexPlusPlusRelayPublishesUnsupportedNotice()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(
            directory,
            "CodexPlusPlus",
            "Codex++ 聚合模式",
            "http://127.0.0.1:8317/v1",
            "relay-secret");
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider>());

        await service.RefreshNowAsync();

        Assert.Null(service.Current.Balance);
        Assert.NotNull(service.Current.UnsupportedReason);
        Assert.Equal("Codex++ · Codex++ 聚合模式", service.Current.SourceName);
        Assert.Equal(QuotaRisk.Safe, service.Current.Risk);
        Assert.DoesNotContain("relay-secret", service.Current.UnsupportedReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshNowAsync_CodexPlusPlusKnownSingleProviderPublishesBalance()
    {
        using var directory = new TemporaryDirectory();
        var configPath = WriteConfig(
            directory,
            "CodexPlusPlus",
            "Codex++ DeepSeek",
            "https://api.deepseek.com/v1",
            "provider-secret");
        var balance = new RecordingBalanceProvider(CreateBalance("deepseek", "DeepSeek", 28m));
        await using var service = CreateService(
            configPath,
            new ThrowingQuotaProvider(),
            new Dictionary<string, IBalanceProvider> { ["deepseek"] = balance });

        await service.RefreshNowAsync();

        Assert.Equal("provider-secret", balance.Credential);
        Assert.Equal(28m, Assert.Single(service.Current.Balance!.Amounts).Amount);
        Assert.Equal("Codex++ · Codex++ DeepSeek", service.Current.SourceName);
        Assert.Null(service.Current.UnsupportedReason);
    }

    [Fact]
    public async Task RefreshNowAsync_CcSwitchRestoredOpenAiConfigReadsOfficialQuota()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.CreateDirectory(".codex"), "config.toml");
        await File.WriteAllTextAsync(configPath, """
            model_provider = "custom"
            [model_providers.custom]
            name = "OpenAI"
            requires_openai_auth = true
            wire_api = "responses"
            """);
        var official = new FixedQuotaProvider(new QuotaSnapshot(
            new QuotaWindow(14, TimeSpan.FromHours(5), Now.AddHours(4)),
            null,
            Now));
        await using var service = CreateService(
            configPath,
            official,
            new Dictionary<string, IBalanceProvider>());

        await service.RefreshNowAsync();

        Assert.Equal(86, service.Current.DisplayPercent);
        Assert.Null(service.Current.Balance);
        Assert.Null(service.Current.UnsupportedReason);
        Assert.Equal(1, official.ReadCount);
    }

    [Fact]
    public async Task RefreshNowAsync_PinnedProviderWithoutCredentialKeepsStaleBalance()
    {
        var provider = new ActiveProvider(
            ActiveProviderMode.CcSwitch,
            "provider-1",
            "DeepSeek",
            new Uri("https://api.deepseek.com"),
            "deepseek",
            null);
        var detector = new SequenceActiveProviderDetector(
            [provider, provider with { IsPinnedToRunningAgent = true }],
            ["secret", null]);
        var balance = new RecordingBalanceProvider(CreateBalance("deepseek", "DeepSeek", 28m));
        await using var service = new QuotaStateService(
            new ThrowingQuotaProvider(),
            detector,
            new Dictionary<string, IBalanceProvider> { ["deepseek"] = balance },
            new FixedTimeProvider(Now));

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(28m, Assert.Single(service.Current.Balance!.Amounts).Amount);
        Assert.True(service.Current.IsStale);
        Assert.Equal("missing-credential", service.Current.Error?.Code);
        Assert.Contains("重启 Agent", service.Current.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_SourceChangeRunsTwoRefreshesThenReturnsToThrottle()
    {
        var deepSeekProvider = new ActiveProvider(
            ActiveProviderMode.Direct,
            "deepseek",
            "DeepSeek",
            new Uri("https://api.deepseek.com"),
            "deepseek",
            null);
        var openRouterProvider = new ActiveProvider(
            ActiveProviderMode.Direct,
            "openrouter",
            "OpenRouter",
            new Uri("https://openrouter.ai/api/v1"),
            "openrouter",
            null);
        var detector = new MutableActiveProviderDetector(deepSeekProvider);
        var deepSeek = new RecordingBalanceProvider(CreateBalance("deepseek", "DeepSeek", 28m));
        var openRouter = new RecordingBalanceProvider(CreateBalance("openrouter", "OpenRouter", 12m));
        await using var service = new QuotaStateService(
            new ThrowingQuotaProvider(),
            detector,
            new Dictionary<string, IBalanceProvider>
            {
                ["deepseek"] = deepSeek,
                ["openrouter"] = openRouter
            },
            refreshInterval: TimeSpan.FromHours(1),
            balanceRefreshInterval: TimeSpan.FromHours(1),
            detectionInterval: TimeSpan.FromMilliseconds(40));

        await service.StartAsync();
        await WaitUntilAsync(() => deepSeek.ReadCount == 2);
        await Task.Delay(120);
        Assert.Equal(2, deepSeek.ReadCount);

        detector.Provider = openRouterProvider;
        await WaitUntilAsync(() => openRouter.ReadCount == 2);

        Assert.Equal("OpenRouter", service.Current.Balance?.DisplayName);
    }

    private static QuotaStateService CreateService(
        string configPath,
        IQuotaProvider official,
        IReadOnlyDictionary<string, IBalanceProvider> providers) => new(
            official,
            new ActiveProviderDetector(configPath),
            providers,
            new FixedTimeProvider(Now));

    private static string WriteConfig(
        TemporaryDirectory directory,
        string providerId,
        string displayName,
        string baseUrl,
        string credential)
    {
        var path = Path.Combine(directory.CreateDirectory(".codex"), "config.toml");
        File.WriteAllText(path, Config(providerId, displayName, baseUrl, credential));
        return path;
    }

    private static string Config(
        string providerId,
        string displayName,
        string baseUrl,
        string credential) => $$"""
        model_provider = "{{providerId}}"
        [model_providers."{{providerId}}"]
        name = "{{displayName}}"
        base_url = "{{baseUrl}}"
        experimental_bearer_token = "{{credential}}"
        """;

    private static BalanceSnapshot CreateBalance(
        string providerId,
        string displayName,
        decimal amount) => new(
            providerId,
            displayName,
            BalanceKind.AccountBalance,
            new[] { new BalanceAmount(amount, "CNY") },
            Now);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }

        Assert.True(condition());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedQuotaProvider(QuotaSnapshot snapshot) : IQuotaProvider
    {
        public int ReadCount { get; private set; }

        public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class ThrowingQuotaProvider : IQuotaProvider
    {
        public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Official quota must not be called.");
    }

    private sealed class RecordingBalanceProvider(BalanceSnapshot snapshot) : IBalanceProvider
    {
        public Uri? BaseUri { get; private set; }

        public string? Credential { get; private set; }

        public int ReadCount { get; private set; }

        public Task<BalanceSnapshot> ReadAsync(
            Uri baseUri,
            string credential,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            BaseUri = baseUri;
            Credential = credential;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class SequenceBalanceProvider(
        params Func<Task<BalanceSnapshot>>[] reads) : IBalanceProvider
    {
        private readonly Queue<Func<Task<BalanceSnapshot>>> _reads = new(reads);

        public Task<BalanceSnapshot> ReadAsync(
            Uri baseUri,
            string credential,
            CancellationToken cancellationToken) => _reads.Dequeue()();
    }

    private sealed class MutableActiveProviderDetector(ActiveProvider provider)
        : IActiveProviderDetector
    {
        public ActiveProvider Provider { get; set; } = provider;

        public ValueTask<ActiveProvider> DetectAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Provider);

        public ValueTask<string?> ReadCredentialAsync(
            ActiveProvider provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("secret");
    }

    private sealed class SequenceActiveProviderDetector(
        IEnumerable<ActiveProvider> providers,
        IEnumerable<string?> credentials) : IActiveProviderDetector
    {
        private readonly Queue<ActiveProvider> _providers = new(providers);
        private readonly Queue<string?> _credentials = new(credentials);

        public ValueTask<ActiveProvider> DetectAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_providers.Dequeue());

        public ValueTask<string?> ReadCredentialAsync(
            ActiveProvider provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_credentials.Dequeue());
    }
}
