using System.Text.Json;
using QuotaOrb.Core.Providers.Detection;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Providers.Detection;

public sealed class ActiveProviderDetectorTests
{
    [Fact]
    public void Detect_MissingModelProviderUsesOfficialCodex()
    {
        var result = ActiveProviderDetector.Detect("model = \"gpt-5\"");

        Assert.Equal(ActiveProviderMode.Official, result.Mode);
        Assert.True(result.IsSupported);
        Assert.Equal("openai", result.ProviderId);
    }

    [Fact]
    public void Detect_CcSwitchRestoredOpenAiConfigUsesOfficialCodex()
    {
        var result = ActiveProviderDetector.Detect("""
            model_provider = "custom"
            [model_providers.custom]
            name = "OpenAI"
            requires_openai_auth = true
            wire_api = "responses"
            """);

        Assert.Equal(ActiveProviderMode.Official, result.Mode);
        Assert.True(result.IsSupported);
        Assert.Equal("openai", result.ProviderId);
    }

    [Fact]
    public void Detect_KnownDirectProviderReturnsNonSensitiveFingerprint()
    {
        const string secret = "sk-do-not-leak";
        var result = ActiveProviderDetector.Detect("""
            model_provider = "deepseek"

            [model_providers.deepseek]
            name = "DeepSeek"
            base_url = "https://API.DeepSeek.com/v1/"
            experimental_bearer_token = "sk-do-not-leak"
            """);

        Assert.Equal(ActiveProviderMode.Direct, result.Mode);
        Assert.True(result.IsSupported);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("https://api.deepseek.com/v1", result.BaseUri!.AbsoluteUri.TrimEnd('/'));
        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_UnknownDirectProviderFailsClosed()
    {
        var result = ActiveProviderDetector.Detect("""
            model_provider = "relay"
            [model_providers.relay]
            base_url = "https://relay.example/v1"
            """);

        Assert.Equal(ActiveProviderMode.Direct, result.Mode);
        Assert.False(result.IsSupported);
        Assert.Null(result.BalanceTemplate);
    }

    [Fact]
    public void Detect_MatchingLoopbackPortUsesCcSwitchSnapshot()
    {
        var ccSwitch = new CcSwitchSnapshot(
            "provider-1",
            "DeepSeek via CC Switch",
            new Uri("https://api.deepseek.com/"),
            "127.0.0.1",
            15721,
            "deepseek",
            null);

        var result = ActiveProviderDetector.Detect("""
            model_provider = "cc-switch"
            [model_providers.cc-switch]
            base_url = "http://localhost:15721/v1"
            """, ccSwitch);

        Assert.Equal(ActiveProviderMode.CcSwitch, result.Mode);
        Assert.True(result.IsSupported);
        Assert.Equal("provider-1", result.ProviderId);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("https://api.deepseek.com/", result.BaseUri!.AbsoluteUri);
    }

    [Fact]
    public void Detect_DifferentLoopbackPortDoesNotClaimCcSwitch()
    {
        var ccSwitch = new CcSwitchSnapshot(
            "provider-1",
            "DeepSeek",
            new Uri("https://api.deepseek.com/"),
            "127.0.0.1",
            15721,
            "balance",
            null);

        var result = ActiveProviderDetector.Detect("""
            model_provider = "relay"
            [model_providers.relay]
            base_url = "http://127.0.0.1:9999/v1"
            """, ccSwitch);

        Assert.Equal(ActiveProviderMode.Direct, result.Mode);
        Assert.False(result.IsSupported);
    }

    [Fact]
    public void Detect_CodexPlusPlusRelayIsUnsupported()
    {
        var result = ActiveProviderDetector.Detect("""
            model_provider = "CodexPlusPlus"
            [model_providers.CodexPlusPlus]
            base_url = "http://127.0.0.1:8317/v1"
            experimental_bearer_token = "relay-secret"
            """);

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, result.Mode);
        Assert.False(result.IsSupported);
        Assert.Null(result.BalanceTemplate);
        Assert.DoesNotContain("relay-secret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_CodexPlusPlusKnownSingleProviderIsSupported()
    {
        var result = ActiveProviderDetector.Detect("""
            model_provider = "CodexPlusPlus"
            [model_providers.CodexPlusPlus]
            name = "Codex++ DeepSeek"
            base_url = "https://api.deepseek.com/v1"
            experimental_bearer_token = "provider-secret"
            """);

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, result.Mode);
        Assert.True(result.IsSupported);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("https://api.deepseek.com/v1", result.BaseUri!.AbsoluteUri.TrimEnd('/'));
        Assert.DoesNotContain("provider-secret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCredentialAsync_RereadsMatchingConfigOnlyOnDemand()
    {
        using var fs = new TemporaryDirectory();
        var configPath = Path.Combine(fs.CreateDirectory(".codex"), "config.toml");
        await File.WriteAllTextAsync(configPath, """
            model_provider = "openrouter"
            [model_providers.openrouter]
            base_url = "https://openrouter.ai/api/v1"
            env_key = "QUOTA_ORB_TEST_KEY"
            """);
        var detector = new ActiveProviderDetector(
            configPath,
            readEnvironmentVariable: name => name == "QUOTA_ORB_TEST_KEY" ? "test-secret" : null);
        var provider = await detector.DetectAsync();

        var credential = await detector.ReadCredentialAsync(provider);

        Assert.Equal("test-secret", credential);
        Assert.DoesNotContain("test-secret", provider.Fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_UsesExistingCodexHomeEnvironmentDirectory()
    {
        using var fs = new TemporaryDirectory();
        var codexHome = fs.CreateDirectory("custom-codex-home");
        await File.WriteAllTextAsync(Path.Combine(codexHome, "config.toml"), """
            model_provider = "deepseek"
            [model_providers.deepseek]
            base_url = "https://api.deepseek.com/v1"
            experimental_bearer_token = "codex-home-secret"
            """);
        var detector = new ActiveProviderDetector(
            readEnvironmentVariable: name => name == "CODEX_HOME" ? codexHome : null,
            codexPlusPlusSettingsPath: Path.Combine(fs.Root, "missing-settings.json"));

        var provider = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.Direct, provider.Mode);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.DoesNotContain("codex-home-secret", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_CurrentCodexPlusPlusResponsesProfileIsSupported()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "pureApi",
            protocol: "responses",
            credential: "current-secret");
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(provider);

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, provider.Mode);
        Assert.True(provider.IsSupported);
        Assert.Equal("deepseek-profile", provider.ProviderId);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.Equal("current-secret", credential);
        Assert.DoesNotContain("current-secret", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_CurrentCodexPlusPlusChatProxyUsesBoundUpstream()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "http://127.0.0.1:57321/v1",
            upstreamBaseUrl: "https://openrouter.ai/api/v1",
            relayMode: "mixedApi",
            protocol: "chatCompletions",
            credential: "chat-secret");
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(provider);

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, provider.Mode);
        Assert.True(provider.IsSupported);
        Assert.Equal("openrouter", provider.BalanceTemplate);
        Assert.Equal("https://openrouter.ai/api/v1", provider.BaseUri!.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("chat-secret", credential);
    }

    [Fact]
    public async Task DetectAsync_CurrentCodexPlusPlusOfficialMixReadsBoundConfigCredential()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "official",
            protocol: "responses",
            credential: "official-mix-secret",
            officialMixApiKey: true);
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(provider);

        Assert.True(provider.IsSupported);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.Equal("official-mix-secret", credential);
    }

    [Fact]
    public async Task DetectAsync_CurrentCodexPlusPlusAggregateFailsClosed()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "http://127.0.0.1:57321/v1",
            upstreamBaseUrl: string.Empty,
            relayMode: "aggregate",
            protocol: "responses",
            credential: "aggregate-sentinel",
            activeAggregateId: "deepseek-profile");
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, provider.Mode);
        Assert.False(provider.IsSupported);
        Assert.Null(provider.BalanceTemplate);
        Assert.Contains("聚合", provider.SupportReason, StringComparison.Ordinal);
        Assert.DoesNotContain("aggregate-sentinel", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_DuplicateCurrentCodexPlusPlusProfileDoesNotOverrideLiveConfig()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "pureApi",
            protocol: "responses",
            credential: "duplicate-secret",
            duplicateProfile: true);
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.Direct, provider.Mode);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.DoesNotContain("duplicate-secret", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectAsync_CurrentCodexPlusPlusUnknownUpstreamIsUnsupported()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://relay.example/v1",
            upstreamBaseUrl: "https://relay.example/v1",
            relayMode: "mixedApi",
            protocol: "responses",
            credential: "relay-secret");
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);

        var provider = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.CodexPlusPlus, provider.Mode);
        Assert.False(provider.IsSupported);
        Assert.Null(provider.BalanceTemplate);
        Assert.DoesNotContain("relay-secret", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCredentialAsync_CurrentCodexPlusPlusProfileChangeReturnsNoCredential()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "pureApi",
            protocol: "responses",
            credential: "old-profile-secret");
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);
        var original = await detector.DetectAsync();

        WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "pureApi",
            protocol: "responses",
            credential: "new-profile-secret",
            profileId: "replacement-profile");
        var credential = await detector.ReadCredentialAsync(original);

        Assert.Null(credential);
    }

    [Fact]
    public async Task ReadCredentialAsync_CurrentOfficialMixRejectsLiveCredentialMismatch()
    {
        using var fs = new TemporaryDirectory();
        var paths = WriteCurrentCodexPlusPlusFixture(
            fs,
            liveBaseUrl: "https://api.deepseek.com/v1",
            upstreamBaseUrl: "https://api.deepseek.com/v1",
            relayMode: "official",
            protocol: "responses",
            credential: "settings-secret",
            officialMixApiKey: true);
        var detector = new ActiveProviderDetector(
            paths.ConfigPath,
            codexPlusPlusSettingsPath: paths.SettingsPath);
        var provider = await detector.DetectAsync();
        await File.WriteAllTextAsync(paths.ConfigPath, CurrentCodexPlusPlusConfig(
            "https://api.deepseek.com/v1",
            "different-live-secret"));

        var credential = await detector.ReadCredentialAsync(provider);

        Assert.Null(credential);
    }

    private static (string ConfigPath, string SettingsPath) WriteCurrentCodexPlusPlusFixture(
        TemporaryDirectory directory,
        string liveBaseUrl,
        string upstreamBaseUrl,
        string relayMode,
        string protocol,
        string credential,
        string activeAggregateId = "",
        bool duplicateProfile = false,
        string profileId = "deepseek-profile",
        bool officialMixApiKey = false)
    {
        var configPath = Path.Combine(directory.CreateDirectory(".codex"), "config.toml");
        File.WriteAllText(
            configPath,
            CurrentCodexPlusPlusConfig(
                liveBaseUrl,
                officialMixApiKey ? credential : null));

        var settingsPath = Path.Combine(
            directory.CreateDirectory(".codex-session-delete"),
            "settings.json");
        var profile = new
        {
            id = profileId,
            name = "Current Codex++ Profile",
            upstreamBaseUrl,
            protocol,
            relayMode,
            officialMixApiKey,
            configContents = $$"""
                model_provider = "custom"
                [model_providers.custom]
                base_url = "{{upstreamBaseUrl}}"
                {{(officialMixApiKey ? $"experimental_bearer_token = \"{credential}\"" : string.Empty)}}
                """,
            authContents = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["OPENAI_API_KEY"] = credential
            })
        };
        var profiles = duplicateProfile ? new[] { profile, profile } : new[] { profile };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
        {
            relayProfilesEnabled = true,
            activeRelayId = profileId,
            activeAggregateRelayId = activeAggregateId,
            relayProfiles = profiles,
            aggregateRelayProfiles = Array.Empty<object>()
        }));

        return (configPath, settingsPath);
    }

    private static string CurrentCodexPlusPlusConfig(
        string baseUrl,
        string? credential = null) => $$"""
        model_provider = "custom"
        [model_providers.custom]
        name = "Codex++"
        base_url = "{{baseUrl}}"
        requires_openai_auth = true
        wire_api = "responses"
        {{(credential is null ? string.Empty : $"experimental_bearer_token = \"{credential}\"")}}
        """;
}
