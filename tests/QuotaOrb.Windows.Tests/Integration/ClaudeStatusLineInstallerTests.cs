using System.Diagnostics;
using System.Text.Json.Nodes;
using QuotaOrb.Windows.Integration;

namespace QuotaOrb.Windows.Tests.Integration;

public sealed class ClaudeStatusLineInstallerTests
{
    [Fact]
    public void EnsureInstalled_AddsBridgeWithoutChangingOtherSettings()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var settings = Path.Combine(root, "settings.json");
            var script = Path.Combine(root, "claude-statusline.ps1");
            var cache = Path.Combine(root, "claude-status.json");
            File.WriteAllText(settings, "{\"env\":{\"ANTHROPIC_BASE_URL\":\"https://example.com\"}}");

            var result = ClaudeStatusLineInstaller.EnsureInstalled(settings, script, cache);

            Assert.Equal(ClaudeStatusLineInstallResult.Installed, result);
            var json = JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
            Assert.Equal(
                "https://example.com",
                json["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
            Assert.Contains("claude-statusline.ps1", json["statusLine"]!["command"]!.GetValue<string>());
            Assert.Contains("rate_limits.five_hour", File.ReadAllText(script));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureInstalled_DoesNotOverwriteExistingStatusLine()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var settings = Path.Combine(root, "settings.json");
            File.WriteAllText(settings, "{\"statusLine\":{\"type\":\"command\",\"command\":\"custom.exe\"}}");

            var result = ClaudeStatusLineInstaller.EnsureInstalled(
                settings,
                Path.Combine(root, "bridge.ps1"),
                Path.Combine(root, "cache.json"));

            Assert.Equal(ClaudeStatusLineInstallResult.ExistingStatusLine, result);
            Assert.Equal(
                "custom.exe",
                JsonNode.Parse(File.ReadAllText(settings))!["statusLine"]!["command"]!
                    .GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureInstalled_DoesNotOverwriteNonObjectStatusLine()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var settings = Path.Combine(root, "settings.json");
            File.WriteAllText(settings, "{\"statusLine\":\"custom-status\"}");

            var result = ClaudeStatusLineInstaller.EnsureInstalled(
                settings,
                Path.Combine(root, "bridge.ps1"),
                Path.Combine(root, "cache.json"));

            Assert.Equal(ClaudeStatusLineInstallResult.ExistingStatusLine, result);
            Assert.Equal(
                "custom-status",
                JsonNode.Parse(File.ReadAllText(settings))!["statusLine"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstalledScript_WritesOnlyRateLimitCache()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var settings = Path.Combine(root, "settings.json");
            var script = Path.Combine(root, "claude-statusline.ps1");
            var cache = Path.Combine(root, "claude-status.json");
            ClaudeStatusLineInstaller.EnsureInstalled(settings, script, cache);
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteAsync("""
                {
                  "session_id": "must-not-be-cached",
                  "rate_limits": {
                    "five_hour": { "used_percentage": 23.5, "resets_at": 1784102400 },
                    "seven_day": { "used_percentage": 41.2, "resets_at": 1784534400 }
                  }
                }
                """);
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(
                process.ExitCode == 0,
                $"PowerShell exited with {process.ExitCode}: {error}");
            Assert.Empty(error);
            Assert.Contains("5h 77%", output);
            var json = await File.ReadAllTextAsync(cache);
            Assert.Contains("fiveHour", json);
            Assert.DoesNotContain("session_id", json);
            Assert.DoesNotContain("must-not-be-cached", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quota-orb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
