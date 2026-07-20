using QuotaOrb.Core.Settings;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_WithNoFile_ReturnsPrivacySafeDefaults()
    {
        using var fs = new TemporaryDirectory();
        var store = new JsonSettingsStore(fs.Root);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.StartWithWindows);
        Assert.True(settings.HideInFullscreen);
        Assert.True(settings.AnimationsEnabled);
        Assert.Equal(AgentSource.Codex, settings.SelectedAgent);
        Assert.Equal(QuotaWindowMode.FiveHour, settings.QuotaWindow);
        Assert.Null(settings.Left);
        Assert.Null(settings.Top);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsCustomPlacementAndOptions()
    {
        using var fs = new TemporaryDirectory();
        var store = new JsonSettingsStore(fs.Root);
        var expected = new AppSettings(
            Left: 123.5,
            Top: 456.25,
            MonitorDeviceName: "DISPLAY2",
            StartWithWindows: false,
            HideInFullscreen: false,
            AnimationsEnabled: false,
            SelectedAgent: AgentSource.ClaudeCode,
            QuotaWindow: QuotaWindowMode.Weekly);

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
        var json = await File.ReadAllTextAsync(
            Path.Combine(fs.Root, "settings.json"),
            CancellationToken.None);
        Assert.Contains("\"startWithWindows\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("QuotaSnapshot", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WithLegacySettings_DefaultsToFiveHourWindow()
    {
        using var fs = new TemporaryDirectory();
        fs.CreateDirectory(".");
        await File.WriteAllTextAsync(
            Path.Combine(fs.Root, "settings.json"),
            """{"selectedAgent":0,"animationsEnabled":true}""");

        var settings = await new JsonSettingsStore(fs.Root).LoadAsync();

        Assert.Equal(QuotaWindowMode.FiveHour, settings.QuotaWindow);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJson_ReturnsDefaultsAndQuarantinesFile()
    {
        using var fs = new TemporaryDirectory();
        fs.CreateDirectory(".");
        await File.WriteAllTextAsync(
            Path.Combine(fs.Root, "settings.json"),
            "{ invalid json",
            CancellationToken.None);
        var store = new JsonSettingsStore(fs.Root);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(new AppSettings(), settings);
        Assert.False(File.Exists(Path.Combine(fs.Root, "settings.json")));
        Assert.True(File.Exists(Path.Combine(fs.Root, "settings.invalid.json")));
    }
}
