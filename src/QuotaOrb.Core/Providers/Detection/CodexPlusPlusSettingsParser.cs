using System.Text.Json;

namespace QuotaOrb.Core.Providers.Detection;

internal sealed record CodexPlusPlusResolution(
    ActiveProvider Provider,
    string? Credential);

internal static class CodexPlusPlusSettingsParser
{
    private const int ProtocolProxyPort = 57321;

    public static CodexPlusPlusResolution? Resolve(
        string settingsJson,
        CodexConfig liveConfig,
        bool includeCredential)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        ArgumentNullException.ThrowIfNull(liveConfig);

        if (!liveConfig.IsValid ||
            !string.Equals(liveConfig.ModelProvider, "custom", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var settings = JsonDocument.Parse(settingsJson);
            var root = settings.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                GetBoolean(root, "relayProfilesEnabled") == false ||
                GetString(root, "activeRelayId") is not { Length: > 0 } activeId ||
                !root.TryGetProperty("relayProfiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            JsonElement? activeProfile = null;
            foreach (var profile in profiles.EnumerateArray())
            {
                if (profile.ValueKind == JsonValueKind.Object &&
                    string.Equals(GetString(profile, "id"), activeId, StringComparison.Ordinal))
                {
                    if (activeProfile is not null)
                    {
                        return null;
                    }

                    activeProfile = profile;
                }
            }

            if (activeProfile is null)
            {
                return null;
            }

            var profileValue = activeProfile.Value;
            var displayName = GetString(profileValue, "name") ?? activeId;
            var relayMode = GetString(profileValue, "relayMode") ?? "mixedApi";
            var protocol = GetString(profileValue, "protocol") ?? "responses";
            var activeAggregateId = GetString(root, "activeAggregateRelayId");
            var upstream = ReadUpstream(profileValue);
            var isAggregate = string.Equals(relayMode, "aggregate", StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(activeAggregateId) &&
                 string.Equals(activeAggregateId, activeId, StringComparison.Ordinal));

            if (isAggregate)
            {
                return MatchesProtocolProxy(liveConfig)
                    ? Unsupported(activeId, "Codex++ 聚合或轮转模式无法确定单一余额。", displayName)
                    : null;
            }

            if (!IsSupportedMode(profileValue, relayMode))
            {
                return MatchesLiveConfig(liveConfig, protocol, upstream)
                    ? Unsupported(
                        activeId,
                        "当前 Codex++ 供应商模式暂不支持。",
                        displayName,
                        upstream)
                    : null;
            }

            if (!MatchesLiveConfig(liveConfig, protocol, upstream))
            {
                return null;
            }

            if (upstream is null || upstream.Scheme != Uri.UriSchemeHttps)
            {
                return Unsupported(activeId, "Codex++ 上游余额地址必须使用 HTTPS。", displayName);
            }

            var template = ActiveProviderDetector.KnownBalanceTemplate(upstream);
            if (template is null)
            {
                return Unsupported(
                    activeId,
                    "Codex++ 当前配置无法绑定已知的单一余额供应商。",
                    displayName,
                    upstream);
            }

            var provider = new ActiveProvider(
                ActiveProviderMode.CodexPlusPlus,
                activeId,
                displayName,
                upstream,
                template,
                null);
            var credential = includeCredential
                ? ReadCredential(profileValue, relayMode)
                : null;
            return new CodexPlusPlusResolution(provider, credential);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsSupportedMode(JsonElement profile, string relayMode) =>
        relayMode is "mixedApi" or "pureApi" ||
        relayMode == "official" && GetBoolean(profile, "officialMixApiKey") == true;

    private static Uri? ReadUpstream(JsonElement profile)
    {
        Uri? upstream;
        var rawUpstream = GetString(profile, "upstreamBaseUrl");
        if (!string.IsNullOrWhiteSpace(rawUpstream))
        {
            return ActiveProviderDetector.TryNormalizeBaseUri(rawUpstream, out upstream)
                ? upstream
                : null;
        }

        var configContents = GetString(profile, "configContents");
        if (configContents is null)
        {
            return null;
        }

        var config = CodexConfigParser.Parse(configContents, includeCredential: false);
        return ActiveProviderDetector.TryNormalizeBaseUri(
            config.ActiveSection?.BaseUrl ?? config.TopLevelBaseUrl,
            out upstream)
            ? upstream
            : null;
    }

    private static bool MatchesLiveConfig(
        CodexConfig liveConfig,
        string protocol,
        Uri? upstream)
    {
        var rawLiveBase = liveConfig.ActiveSection?.BaseUrl ?? liveConfig.TopLevelBaseUrl;
        if (!ActiveProviderDetector.TryNormalizeBaseUri(rawLiveBase, out var liveBase))
        {
            return false;
        }

        return protocol switch
        {
            "responses" => upstream is not null && SameEndpoint(liveBase!, upstream),
            "chatCompletions" => MatchesProtocolProxy(liveBase!),
            _ => false
        };
    }

    private static bool MatchesProtocolProxy(CodexConfig liveConfig)
    {
        var rawLiveBase = liveConfig.ActiveSection?.BaseUrl ?? liveConfig.TopLevelBaseUrl;
        return ActiveProviderDetector.TryNormalizeBaseUri(rawLiveBase, out var liveBase) &&
            MatchesProtocolProxy(liveBase!);
    }

    private static bool MatchesProtocolProxy(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal) &&
        uri.Port == ProtocolProxyPort &&
        string.Equals(uri.AbsolutePath.TrimEnd('/'), "/v1", StringComparison.Ordinal);

    private static bool SameEndpoint(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port &&
        string.Equals(left.AbsolutePath, right.AbsolutePath, StringComparison.Ordinal);

    private static string? ReadCredential(JsonElement profile, string relayMode)
    {
        var configCredential = ReadConfigCredential(GetString(profile, "configContents"));
        if (relayMode == "official")
        {
            return configCredential;
        }

        return ReadAuthCredential(GetString(profile, "authContents")) ?? configCredential;
    }

    private static string? ReadConfigCredential(string? configContents)
    {
        if (configContents is null)
        {
            return null;
        }

        return CodexConfigParser.Parse(configContents, includeCredential: true)
            .ReadCredential(_ => null);
    }

    private static string? ReadAuthCredential(string? authContents)
    {
        if (authContents is null)
        {
            return null;
        }

        try
        {
            using var auth = JsonDocument.Parse(authContents);
            return GetString(auth.RootElement, "OPENAI_API_KEY");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CodexPlusPlusResolution Unsupported(
        string providerId,
        string reason,
        string? displayName = null,
        Uri? upstream = null) => new(
            new ActiveProvider(
                ActiveProviderMode.CodexPlusPlus,
                providerId,
                displayName ?? providerId,
                upstream,
                null,
                reason),
            null);

    private static string? GetString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static bool? GetBoolean(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
