using System.Text.Json;

namespace QuotaOrb.Core.Providers.Detection;

public sealed class ClaudeActiveProviderDetector : IActiveProviderDetector
{
    private const long MaxSettingsBytes = 1024 * 1024;
    private readonly string _settingsPath;
    private readonly IClaudeCcSwitchDataSource? _ccSwitch;
    private readonly string _ccSwitchAppType;
    private readonly Func<bool> _hasRunningAgent;

    public ClaudeActiveProviderDetector(
        string? settingsPath = null,
        IClaudeCcSwitchDataSource? ccSwitch = null,
        string ccSwitchAppType = "claude",
        Func<bool>? hasRunningAgent = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
        _ccSwitch = ccSwitch;
        _ccSwitchAppType = ccSwitchAppType is "claude" or "claude-desktop"
            ? ccSwitchAppType
            : throw new ArgumentOutOfRangeException(nameof(ccSwitchAppType));
        _hasRunningAgent = hasRunningAgent ?? (() => false);
    }

    public async ValueTask<ActiveProvider> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await ReadSettingsAsync(includeCredential: false, cancellationToken)
            .ConfigureAwait(false);
        if (settings.Error is not null)
        {
            return Unsupported(settings.Error);
        }

        if (settings.BaseUri is null)
        {
            var restoredProvider = await ReadRunningAgentCcSwitchProviderAsync(cancellationToken)
                .ConfigureAwait(false);
            if (restoredProvider is not null)
            {
                return restoredProvider;
            }

            return Official();
        }

        if (settings.BaseUri.IsLoopback && _ccSwitch is not null)
        {
            try
            {
                var snapshot = await _ccSwitch
                    .ReadCurrentClaudeProviderAsync(_ccSwitchAppType, cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is not null && snapshot.MatchesProxy(settings.BaseUri))
                {
                    return FromCcSwitch(snapshot);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Unsupported("无法安全读取 CC Switch Claude 数据。");
            }

            return Unsupported("当前 Claude 本地代理无法绑定已知供应商。");
        }

        if (string.Equals(
                settings.BaseUri.IdnHost,
                "api.anthropic.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return Official();
        }

        var template = ActiveProviderDetector.KnownBalanceTemplate(settings.BaseUri);
        return new ActiveProvider(
            ActiveProviderMode.Direct,
            template ?? settings.BaseUri.IdnHost,
            template switch
            {
                "deepseek" => "DeepSeek",
                "openrouter" => "OpenRouter",
                _ => settings.BaseUri.IdnHost
            },
            settings.BaseUri,
            template,
            template is null ? "当前 Claude 第三方 API 尚无内置余额适配器。" : null);
    }

    public async ValueTask<string?> ReadCredentialAsync(
        ActiveProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider.Mode == ActiveProviderMode.CcSwitch)
        {
            return _ccSwitch is null
                ? null
                : await _ccSwitch
                    .ReadCurrentClaudeCredentialAsync(
                        _ccSwitchAppType,
                        provider.ProviderId,
                        cancellationToken)
                    .ConfigureAwait(false);
        }

        return provider.Mode == ActiveProviderMode.Direct
            ? (await ReadSettingsAsync(includeCredential: true, cancellationToken)
                .ConfigureAwait(false)).Credential
            : null;
    }

    private async ValueTask<ActiveProvider?> ReadRunningAgentCcSwitchProviderAsync(
        CancellationToken cancellationToken)
    {
        if (_ccSwitch is null || !HasRunningAgent())
        {
            return null;
        }

        try
        {
            var snapshot = await _ccSwitch
                .ReadCurrentClaudeProviderAsync(_ccSwitchAppType, cancellationToken)
                .ConfigureAwait(false);
            return snapshot is null ? null : FromCcSwitch(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private bool HasRunningAgent()
    {
        try
        {
            return _hasRunningAgent();
        }
        catch
        {
            return false;
        }
    }

    private async Task<ClaudeSettings> ReadSettingsAsync(
        bool includeCredential,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(_settingsPath);
            if (!file.Exists)
            {
                return new ClaudeSettings(null, null, null);
            }

            if (file.Length > MaxSettingsBytes)
            {
                return new ClaudeSettings(null, null, "Claude 设置文件过大。");
            }

            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                useAsync: true);
            using var json = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("env", out var env) ||
                env.ValueKind != JsonValueKind.Object)
            {
                return new ClaudeSettings(null, null, null);
            }

            var rawBaseUrl = GetString(env, "ANTHROPIC_BASE_URL");
            Uri? baseUri = null;
            if (!string.IsNullOrWhiteSpace(rawBaseUrl) &&
                !ActiveProviderDetector.TryNormalizeBaseUri(rawBaseUrl, out baseUri))
            {
                return new ClaudeSettings(null, null, "Claude Base URL 无效或不安全。");
            }

            var credential = includeCredential
                ? GetString(env, "ANTHROPIC_AUTH_TOKEN") ??
                  GetString(env, "ANTHROPIC_API_KEY")
                : null;
            return new ClaudeSettings(baseUri, credential, null);
        }
        catch (JsonException)
        {
            return new ClaudeSettings(null, null, "Claude 设置文件不是有效 JSON。");
        }
        catch (IOException)
        {
            return new ClaudeSettings(null, null, "无法读取 Claude 设置文件。");
        }
        catch (UnauthorizedAccessException)
        {
            return new ClaudeSettings(null, null, "无法读取 Claude 设置文件。");
        }
    }

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static ActiveProvider Official() => new(
        ActiveProviderMode.Official,
        "claude-official",
        "Claude 官方",
        null,
        null,
        null);

    private static ActiveProvider FromCcSwitch(ClaudeCcSwitchSnapshot snapshot) =>
        snapshot.IsOfficial
            ? Official()
            : new ActiveProvider(
                ActiveProviderMode.CcSwitch,
                snapshot.ProviderId,
                snapshot.DisplayName,
                snapshot.UpstreamBaseUri,
                snapshot.BalanceTemplate,
                snapshot.SupportReason);

    private static ActiveProvider Unsupported(string reason) => new(
        ActiveProviderMode.Unsupported,
        "claude-unknown",
        "Claude",
        null,
        null,
        reason);

    private sealed record ClaudeSettings(Uri? BaseUri, string? Credential, string? Error);
}
