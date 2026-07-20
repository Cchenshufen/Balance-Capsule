using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuotaOrb.Windows.Integration;

public enum ClaudeStatusLineInstallResult
{
    Installed,
    AlreadyInstalled,
    ExistingStatusLine
}

public static class ClaudeStatusLineInstaller
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static ClaudeStatusLineInstallResult EnsureInstalled(
        string? settingsPath = null,
        string? scriptPath = null,
        string? cachePath = null)
    {
        settingsPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
        scriptPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaOrb",
            "claude-statusline.ps1");
        cachePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaOrb",
            "claude-status.json");

        var scriptDirectory = Path.GetDirectoryName(scriptPath)
            ?? throw new InvalidOperationException("Claude status-line script path has no directory.");
        Directory.CreateDirectory(scriptDirectory);
        File.WriteAllText(
            scriptPath,
            CreateScript(cachePath),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var settingsDirectory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("Claude settings path has no directory.");
        Directory.CreateDirectory(settingsDirectory);

        JsonObject root;
        if (File.Exists(settingsPath))
        {
            root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                ?? throw new JsonException("Claude settings JSON must be an object.");
        }
        else
        {
            root = new JsonObject();
        }

        var command = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        if (root["statusLine"] is { } statusLine)
        {
            if (statusLine is not JsonObject existing
                || !string.Equals(
                    existing["command"]?.GetValue<string>(),
                    command,
                    StringComparison.OrdinalIgnoreCase))
            {
                return ClaudeStatusLineInstallResult.ExistingStatusLine;
            }

            return ClaudeStatusLineInstallResult.AlreadyInstalled;
        }

        root["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["refreshInterval"] = 60
        };

        var temporaryPath = settingsPath + ".quota-orb.tmp";
        var backupPath = settingsPath + ".quota-orb.backup";
        try
        {
            if (File.Exists(settingsPath))
            {
                File.Copy(settingsPath, backupPath, overwrite: true);
            }

            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(SerializerOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return ClaudeStatusLineInstallResult.Installed;
    }

    private static string CreateScript(string cachePath)
    {
        var escapedCachePath = cachePath.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
                $five = $payload.rate_limits.five_hour
                $seven = $payload.rate_limits.seven_day
                if ($null -ne $five -or $null -ne $seven) {
                    $fiveCache = if ($null -eq $five) { $null } else { [ordered]@{
                        usedPercentage = [double]$five.used_percentage
                        resetsAt = [DateTimeOffset]::FromUnixTimeSeconds([long]$five.resets_at).ToString('O')
                    } }
                    $sevenCache = if ($null -eq $seven) { $null } else { [ordered]@{
                        usedPercentage = [double]$seven.used_percentage
                        resetsAt = [DateTimeOffset]::FromUnixTimeSeconds([long]$seven.resets_at).ToString('O')
                    } }
                    $cache = [ordered]@{
                        capturedAt = [DateTimeOffset]::UtcNow.ToString('O')
                        fiveHour = $fiveCache
                        sevenDay = $sevenCache
                    }
                    $path = '{{escapedCachePath}}'
                    $temporary = "$path.$PID.tmp"
                    $cache | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporary -Encoding UTF8
                    Move-Item -LiteralPath $temporary -Destination $path -Force
                }
                $values = @()
                if ($null -ne $five) { $values += ('5h {0:0}%' -f (100 - [double]$five.used_percentage)) }
                if ($null -ne $seven) { $values += ('7d {0:0}%' -f (100 - [double]$seven.used_percentage)) }
                if ($values.Count -eq 0) { 'Claude 用量待同步' } else { 'Claude ' + ($values -join ' · ') }
            }
            catch {
                'Claude 用量同步失败'
            }
            """;
    }
}
