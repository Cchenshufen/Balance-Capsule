using System.Text.Json;

namespace QuotaOrb.Core.Providers.Detection;

public sealed class ActiveProviderDetector : IActiveProviderDetector
{
    private const long MaxCodexPlusPlusSettingsBytes = 4 * 1024 * 1024;

    private readonly string _configPath;
    private readonly string _codexPlusPlusSettingsPath;
    private readonly ICcSwitchDataSource? _ccSwitch;
    private readonly Func<string, string?> _readEnvironmentVariable;

    public ActiveProviderDetector(
        string? configPath = null,
        ICcSwitchDataSource? ccSwitch = null,
        Func<string, string?>? readEnvironmentVariable = null,
        string? codexPlusPlusSettingsPath = null)
    {
        _readEnvironmentVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _configPath = configPath ?? ResolveDefaultConfigPath(_readEnvironmentVariable);
        _codexPlusPlusSettingsPath = codexPlusPlusSettingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex-session-delete",
            "settings.json");
        _ccSwitch = ccSwitch;
    }

    public async ValueTask<ActiveProvider> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
        {
            return Official();
        }

        string toml;
        try
        {
            toml = await File.ReadAllTextAsync(_configPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Unsupported("无法读取 Codex 配置。");
        }

        var config = CodexConfigParser.Parse(toml, includeCredential: false);
        var codexPlusPlus = await ReadCodexPlusPlusAsync(
            config,
            includeCredential: false,
            cancellationToken).ConfigureAwait(false);
        if (codexPlusPlus is not null)
        {
            return codexPlusPlus.Provider;
        }

        var result = Resolve(config, null);
        if (result.Mode != ActiveProviderMode.Direct || result.BaseUri?.IsLoopback != true || _ccSwitch is null)
        {
            return result;
        }

        try
        {
            var ccSwitch = await _ccSwitch
                .ReadCurrentCodexProviderAsync(cancellationToken)
                .ConfigureAwait(false);
            return Resolve(config, ccSwitch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return result with { SupportReason = "无法安全读取 CC Switch 数据。" };
        }
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
                    .ReadCurrentCodexCredentialAsync(provider.ProviderId, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (provider.Mode is not (ActiveProviderMode.Direct or ActiveProviderMode.CodexPlusPlus))
        {
            return null;
        }

        var toml = await File.ReadAllTextAsync(_configPath, cancellationToken).ConfigureAwait(false);
        var config = CodexConfigParser.Parse(toml, includeCredential: true);
        if (provider.Mode == ActiveProviderMode.CodexPlusPlus)
        {
            var settingsJson = await ReadCodexPlusPlusSettingsAsync(cancellationToken)
                .ConfigureAwait(false);
            var codexPlusPlus = settingsJson is null
                ? null
                : CodexPlusPlusSettingsParser.Resolve(
                    settingsJson,
                    config,
                    includeCredential: true);
            if (codexPlusPlus is not null &&
                string.Equals(
                    codexPlusPlus.Provider.Fingerprint,
                    provider.Fingerprint,
                    StringComparison.Ordinal))
            {
                var liveCredential = config.ReadCredential(_readEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(liveCredential) &&
                    !string.Equals(
                        liveCredential,
                        codexPlusPlus.Credential,
                        StringComparison.Ordinal))
                {
                    return null;
                }

                var stableToml = await File.ReadAllTextAsync(_configPath, cancellationToken)
                    .ConfigureAwait(false);
                var stableSettingsJson = await ReadCodexPlusPlusSettingsAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (stableSettingsJson is null ||
                    !string.Equals(toml, stableToml, StringComparison.Ordinal) ||
                    !string.Equals(settingsJson, stableSettingsJson, StringComparison.Ordinal))
                {
                    return null;
                }

                var stableConfig = CodexConfigParser.Parse(
                    stableToml,
                    includeCredential: false);
                var stableProvider = CodexPlusPlusSettingsParser.Resolve(
                    stableSettingsJson,
                    stableConfig,
                    includeCredential: false)?.Provider;
                return stableProvider is not null &&
                    string.Equals(
                        stableProvider.Fingerprint,
                        provider.Fingerprint,
                        StringComparison.Ordinal)
                    ? codexPlusPlus.Credential
                    : null;
            }
        }

        var current = Resolve(config, null);
        if (!string.Equals(current.Fingerprint, provider.Fingerprint, StringComparison.Ordinal))
        {
            return null;
        }

        return config.ReadCredential(_readEnvironmentVariable);
    }

    private async ValueTask<CodexPlusPlusResolution?> ReadCodexPlusPlusAsync(
        CodexConfig liveConfig,
        bool includeCredential,
        CancellationToken cancellationToken)
    {
        var json = await ReadCodexPlusPlusSettingsAsync(cancellationToken).ConfigureAwait(false);
        return json is null
            ? null
            : CodexPlusPlusSettingsParser.Resolve(json, liveConfig, includeCredential);
    }

    private async ValueTask<string?> ReadCodexPlusPlusSettingsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(_codexPlusPlusSettingsPath);
            if (!file.Exists || file.Length > MaxCodexPlusPlusSettingsBytes)
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(
                _codexPlusPlusSettingsPath,
                cancellationToken).ConfigureAwait(false);
            if (json.Length > MaxCodexPlusPlusSettingsBytes)
            {
                return null;
            }

            return json;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveDefaultConfigPath(
        Func<string, string?> readEnvironmentVariable)
    {
        var configuredHome = readEnvironmentVariable("CODEX_HOME");
        var codexHome = !string.IsNullOrWhiteSpace(configuredHome) &&
            Directory.Exists(configuredHome)
                ? configuredHome
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex");
        return Path.Combine(codexHome, "config.toml");
    }

    public static ActiveProvider Detect(string toml, CcSwitchSnapshot? ccSwitch = null) =>
        Resolve(CodexConfigParser.Parse(toml, includeCredential: false), ccSwitch);

    internal static bool TryNormalizeBaseUri(string? value, out Uri? result)
    {
        result = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var builder = new UriBuilder(uri!)
        {
            Host = uri.Host.ToLowerInvariant(),
            Path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : "/"
        };
        result = builder.Uri;
        return true;
    }

    public static string? KnownBalanceTemplate(Uri baseUri) =>
        baseUri.Host.ToLowerInvariant() switch
        {
            "api.deepseek.com" => "deepseek",
            "openrouter.ai" => "openrouter",
            _ => null
        };

    private static ActiveProvider Resolve(CodexConfig config, CcSwitchSnapshot? ccSwitch)
    {
        if (!config.IsValid)
        {
            return Unsupported("Codex 供应商配置无效或暂不支持。");
        }

        if (!config.HasModelProvider ||
            string.Equals(config.ModelProvider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return Official();
        }

        var providerId = config.ModelProvider!;
        var section = config.ActiveSection;
        var displayName = string.IsNullOrWhiteSpace(section?.Name) ? providerId : section.Name!;

        if (string.Equals(providerId, "custom", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(section?.Name, "OpenAI", StringComparison.OrdinalIgnoreCase) &&
            section?.RequiresOpenAiAuth == true &&
            string.IsNullOrWhiteSpace(section?.BaseUrl) &&
            string.IsNullOrWhiteSpace(config.TopLevelBaseUrl))
        {
            return Official();
        }

        if (!TryNormalizeBaseUri(section?.BaseUrl ?? config.TopLevelBaseUrl, out var baseUri))
        {
            return new ActiveProvider(
                ActiveProviderMode.Unsupported,
                providerId,
                displayName,
                null,
                null,
                "当前供应商缺少安全的 Base URL。");
        }

        if (string.Equals(providerId, "CodexPlusPlus", StringComparison.OrdinalIgnoreCase))
        {
            var codexPlusPlusTemplate = KnownBalanceTemplate(baseUri!);
            var codexPlusPlusReason = baseUri!.IsLoopback
                ? "Codex++ 本地中转无法安全确定单一上游余额。"
                : baseUri.Scheme != Uri.UriSchemeHttps
                    ? "Codex++ 上游余额地址必须使用 HTTPS。"
                    : codexPlusPlusTemplate is null
                        ? "Codex++ 当前配置无法绑定已知的单一余额供应商。"
                        : null;

            return new ActiveProvider(
                ActiveProviderMode.CodexPlusPlus,
                providerId,
                displayName,
                baseUri,
                codexPlusPlusReason is null ? codexPlusPlusTemplate : null,
                codexPlusPlusReason);
        }

        if (baseUri!.IsLoopback && ccSwitch is not null && ccSwitch.MatchesProxy(baseUri))
        {
            return new ActiveProvider(
                ActiveProviderMode.CcSwitch,
                ccSwitch.ProviderId,
                ccSwitch.DisplayName,
                ccSwitch.UpstreamBaseUri,
                ccSwitch.BalanceTemplate,
                ccSwitch.SupportReason);
        }

        var template = KnownBalanceTemplate(baseUri);
        var reason = baseUri.IsLoopback
            ? "该本地中转不是已验证的 CC Switch Codex 代理。"
            : baseUri.Scheme != Uri.UriSchemeHttps
                ? "第三方余额查询必须使用 HTTPS。"
                : template is null
                    ? "该供应商需要明确配置余额查询。"
                    : null;

        return new ActiveProvider(
            ActiveProviderMode.Direct,
            providerId,
            displayName,
            baseUri,
            template,
            reason);
    }

    private static ActiveProvider Official() => new(
        ActiveProviderMode.Official,
        "openai",
        "OpenAI",
        null,
        null,
        null);

    private static ActiveProvider Unsupported(string reason) => new(
        ActiveProviderMode.Unsupported,
        "unknown",
        "Unknown",
        null,
        null,
        reason);
}

internal sealed class CodexConfig
{
    public bool IsValid { get; set; } = true;
    public bool HasModelProvider { get; set; }
    public string? ModelProvider { get; set; }
    public string? TopLevelBaseUrl { get; set; }
    public string? TopLevelCredential { get; set; }
    public Dictionary<string, CodexProviderSection> Providers { get; } = new(StringComparer.Ordinal);

    public CodexProviderSection? ActiveSection =>
        ModelProvider is not null && Providers.TryGetValue(ModelProvider, out var section)
            ? section
            : null;

    public string? ReadCredential(Func<string, string?> readEnvironmentVariable)
    {
        var section = ActiveSection;
        if (!string.IsNullOrWhiteSpace(section?.Credential))
        {
            return section.Credential;
        }

        if (!string.IsNullOrWhiteSpace(section?.EnvironmentKey))
        {
            return readEnvironmentVariable(section.EnvironmentKey!);
        }

        return string.IsNullOrWhiteSpace(TopLevelCredential) ? null : TopLevelCredential;
    }
}

internal sealed class CodexProviderSection
{
    public string? Name { get; set; }
    public string? BaseUrl { get; set; }
    public string? EnvironmentKey { get; set; }
    public string? Credential { get; set; }
    public bool? RequiresOpenAiAuth { get; set; }
}

internal static class CodexConfigParser
{
    public static CodexConfig Parse(string toml, bool includeCredential)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var result = new CodexConfig();
        CodexProviderSection? currentProvider = null;
        var inOtherTable = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawLine in toml.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('['))
            {
                currentProvider = TryParseProviderHeader(line, out var providerId)
                    ? result.Providers.GetValueOrDefault(providerId!) ?? AddProvider(result, providerId!)
                    : null;
                inOtherTable = currentProvider is null;
                continue;
            }

            if (!TrySplitAssignment(line, out var key, out var rawValue))
            {
                continue;
            }

            if (currentProvider is not null)
            {
                ReadProviderValue(result, currentProvider, key, rawValue, seen, includeCredential);
            }
            else if (!inOtherTable)
            {
                ReadRootValue(result, key, rawValue, seen, includeCredential);
            }
        }

        if (result.HasModelProvider && string.IsNullOrWhiteSpace(result.ModelProvider))
        {
            result.IsValid = false;
        }

        return result;
    }

    private static CodexProviderSection AddProvider(CodexConfig config, string providerId)
    {
        var section = new CodexProviderSection();
        config.Providers.Add(providerId, section);
        return section;
    }

    private static void ReadRootValue(
        CodexConfig config,
        string key,
        string rawValue,
        HashSet<string> seen,
        bool includeCredential)
    {
        switch (key)
        {
            case "model_provider":
                config.HasModelProvider = true;
                config.ModelProvider = ReadUniqueString(config, seen, "root:model_provider", rawValue);
                break;
            case "base_url":
                config.TopLevelBaseUrl = ReadUniqueString(config, seen, "root:base_url", rawValue);
                break;
            case "experimental_bearer_token" when includeCredential:
                config.TopLevelCredential = ReadUniqueString(
                    config,
                    seen,
                    "root:experimental_bearer_token",
                    rawValue);
                break;
        }
    }

    private static void ReadProviderValue(
        CodexConfig config,
        CodexProviderSection section,
        string key,
        string rawValue,
        HashSet<string> seen,
        bool includeCredential)
    {
        var prefix = $"provider:{config.Providers.First(pair => ReferenceEquals(pair.Value, section)).Key}:";
        switch (key)
        {
            case "name":
                section.Name = ReadUniqueString(config, seen, prefix + key, rawValue);
                break;
            case "base_url":
                section.BaseUrl = ReadUniqueString(config, seen, prefix + key, rawValue);
                break;
            case "env_key":
                section.EnvironmentKey = ReadUniqueString(config, seen, prefix + key, rawValue);
                break;
            case "requires_openai_auth":
                if (!seen.Add(prefix + key) || !bool.TryParse(rawValue, out var requiresOpenAiAuth))
                {
                    config.IsValid = false;
                }
                else
                {
                    section.RequiresOpenAiAuth = requiresOpenAiAuth;
                }
                break;
            case "experimental_bearer_token" when includeCredential:
                section.Credential = ReadUniqueString(config, seen, prefix + key, rawValue);
                break;
        }
    }

    private static string? ReadUniqueString(
        CodexConfig config,
        HashSet<string> seen,
        string identity,
        string rawValue)
    {
        if (!seen.Add(identity) || !TryParseString(rawValue, out var value))
        {
            config.IsValid = false;
            return null;
        }

        return value;
    }

    private static bool TryParseProviderHeader(string line, out string? providerId)
    {
        providerId = null;
        if (!line.EndsWith(']') || line.StartsWith("[[", StringComparison.Ordinal))
        {
            return false;
        }

        const string prefix = "model_providers.";
        var inner = line[1..^1].Trim();
        if (!inner.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var key = inner[prefix.Length..].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        if (key[0] is '\'' or '"')
        {
            return TryParseString(key, out providerId) && !string.IsNullOrWhiteSpace(providerId);
        }

        if (key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            return false;
        }

        providerId = key;
        return true;
    }

    private static bool TrySplitAssignment(string line, out string key, out string value)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quote == '"' && character == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"' && !escaped)
            {
                quote = quote == '\0' ? character : quote == character ? '\0' : quote;
            }

            if (character == '=' && quote == '\0')
            {
                key = line[..index].Trim();
                value = line[(index + 1)..].Trim();
                return key.Length > 0;
            }

            escaped = false;
        }

        key = string.Empty;
        value = string.Empty;
        return false;
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (quote == '"' && character == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"' && !escaped)
            {
                quote = quote == '\0' ? character : quote == character ? '\0' : quote;
            }
            else if (character == '#' && quote == '\0')
            {
                return line[..index];
            }

            escaped = false;
        }

        return line;
    }

    private static bool TryParseString(string value, out string? result)
    {
        result = null;
        value = value.Trim();
        if (value.Length < 2)
        {
            return false;
        }

        if (value[0] == '\'' && value[^1] == '\'' && !value.StartsWith("'''", StringComparison.Ordinal))
        {
            result = value[1..^1];
            return true;
        }

        if (value[0] != '"' || value[^1] != '"' || value.StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            result = JsonSerializer.Deserialize<string>(value);
            return result is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
