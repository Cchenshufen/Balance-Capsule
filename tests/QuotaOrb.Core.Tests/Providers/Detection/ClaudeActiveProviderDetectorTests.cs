using QuotaOrb.Core.Providers.Detection;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Providers.Detection;

public sealed class ClaudeActiveProviderDetectorTests
{
    [Fact]
    public async Task DetectAsync_WithNoBaseUrl_UsesOfficialClaude()
    {
        using var directory = new TemporaryDirectory();
        var settings = WriteSettings(directory, "{}");
        var detector = new ClaudeActiveProviderDetector(settings);

        var result = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.Official, result.Mode);
        Assert.Equal("Claude 官方", result.DisplayName);
    }

    [Fact]
    public async Task DetectAsync_WithRestoredSettingsAndRunningAgent_UsesLastCcSwitchProvider()
    {
        using var directory = new TemporaryDirectory();
        var settings = WriteSettings(directory, "{}");
        var ccSwitch = new FixedClaudeCcSwitchDataSource(new ClaudeCcSwitchSnapshot(
            "deepseek",
            "DeepSeek",
            new Uri("https://api.deepseek.com/anthropic"),
            "127.0.0.1",
            15721,
            "deepseek",
            null,
            IsOfficial: false),
            "secret");
        var detector = new ClaudeActiveProviderDetector(
            settings,
            ccSwitch,
            hasRunningAgent: () => true);

        var result = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(result);

        Assert.Equal(ActiveProviderMode.CcSwitch, result.Mode);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("secret", credential);
    }

    [Fact]
    public async Task DetectAsync_WithDirectOpenRouter_UsesBalanceAdapter()
    {
        using var directory = new TemporaryDirectory();
        var settings = WriteSettings(directory, """
            {
              "env": {
                "ANTHROPIC_BASE_URL": "https://openrouter.ai/api/v1",
                "ANTHROPIC_AUTH_TOKEN": "secret"
              }
            }
            """);
        var detector = new ClaudeActiveProviderDetector(settings);

        var result = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(result);

        Assert.Equal(ActiveProviderMode.Direct, result.Mode);
        Assert.Equal("openrouter", result.BalanceTemplate);
        Assert.Equal("secret", credential);
    }

    [Fact]
    public async Task DetectAsync_WithMatchingOfficialCcSwitchProxy_UsesOfficialClaude()
    {
        using var directory = new TemporaryDirectory();
        var settings = WriteSettings(directory, """
            { "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:15721" } }
            """);
        var ccSwitch = new FixedClaudeCcSwitchDataSource(new ClaudeCcSwitchSnapshot(
            "claude-official",
            "Claude Official",
            null,
            "127.0.0.1",
            15721,
            null,
            null,
            IsOfficial: true));
        var detector = new ClaudeActiveProviderDetector(settings, ccSwitch);

        var result = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.Official, result.Mode);
    }

    [Fact]
    public async Task DetectAsync_WithDesktopCcSwitch_UsesDesktopProviderAndCredential()
    {
        using var directory = new TemporaryDirectory();
        var settings = WriteSettings(directory, """
            { "env": { "ANTHROPIC_BASE_URL": "http://127.0.0.1:15721" } }
            """);
        var ccSwitch = new FixedClaudeCcSwitchDataSource(new ClaudeCcSwitchSnapshot(
            "deepseek-desktop",
            "DeepSeek",
            new Uri("https://api.deepseek.com/anthropic"),
            "127.0.0.1",
            15721,
            "deepseek",
            null,
            IsOfficial: false),
            "secret");
        var detector = new ClaudeActiveProviderDetector(
            settings,
            ccSwitch,
            ccSwitchAppType: "claude-desktop");

        var result = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(result);

        Assert.Equal(ActiveProviderMode.CcSwitch, result.Mode);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("secret", credential);
        Assert.Equal("claude-desktop", ccSwitch.LastProviderAppType);
        Assert.Equal("claude-desktop", ccSwitch.LastCredentialAppType);
    }

    [Fact]
    public void ClaudeCcSwitchParser_MapsKnownProviderAndCredential()
    {
        var row = new CcSwitchDatabaseRow(
            "openrouter",
            "OpenRouter",
            """
            {
              "env": {
                "ANTHROPIC_BASE_URL": "https://openrouter.ai/api/v1",
                "ANTHROPIC_AUTH_TOKEN": "secret"
              }
            }
            """,
            "{}",
            "127.0.0.1",
            15721);

        var result = ClaudeCcSwitchProviderParser.Parse(row);

        Assert.Equal("openrouter", result.BalanceTemplate);
        Assert.Null(result.SupportReason);
        Assert.Equal("secret", ClaudeCcSwitchProviderParser.ReadCredential(row));
    }

    private static string WriteSettings(TemporaryDirectory directory, string json)
    {
        directory.CreateDirectory(".");
        var path = Path.Combine(directory.Root, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class FixedClaudeCcSwitchDataSource(
        ClaudeCcSwitchSnapshot snapshot,
        string? credential = null)
        : IClaudeCcSwitchDataSource
    {
        public string? LastProviderAppType { get; private set; }

        public string? LastCredentialAppType { get; private set; }

        public ValueTask<ClaudeCcSwitchSnapshot?> ReadCurrentClaudeProviderAsync(
            string appType,
            CancellationToken cancellationToken = default)
        {
            LastProviderAppType = appType;
            return ValueTask.FromResult<ClaudeCcSwitchSnapshot?>(snapshot);
        }

        public ValueTask<string?> ReadCurrentClaudeCredentialAsync(
            string appType,
            string providerId,
            CancellationToken cancellationToken = default)
        {
            LastCredentialAppType = appType;
            return ValueTask.FromResult(credential);
        }
    }
}
