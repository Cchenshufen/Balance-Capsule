using System.Text.Json;

namespace QuotaOrb.Core.Providers.Detection;

public interface ICcSwitchDataSource
{
    ValueTask<CcSwitchSnapshot?> ReadCurrentCodexProviderAsync(
        CancellationToken cancellationToken = default);

    ValueTask<string?> ReadCurrentCodexCredentialAsync(
        string providerId,
        CancellationToken cancellationToken = default);
}

public sealed record CcSwitchDatabaseRow(
    string ProviderId,
    string DisplayName,
    string SettingsConfigJson,
    string MetaJson,
    string ListenAddress,
    int ListenPort);

public sealed record CcSwitchSnapshot(
    string ProviderId,
    string DisplayName,
    Uri? UpstreamBaseUri,
    string ListenAddress,
    int ListenPort,
    string? BalanceTemplate,
    string? SupportReason)
{
    public bool MatchesProxy(Uri baseUri) =>
        baseUri.IsLoopback && baseUri.Port == ListenPort;
}

public static class CcSwitchProviderParser
{
    public const string CurrentCodexProviderQuery = """
        SELECT p.id, p.name, p.settings_config, p.meta,
               pc.listen_address, pc.listen_port
        FROM providers AS p
        JOIN proxy_config AS pc ON pc.app_type = p.app_type
        WHERE p.app_type = 'codex' AND p.is_current = 1
        LIMIT 1
        """;

    public static CcSwitchSnapshot Parse(CcSwitchDatabaseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.ProviderId) ||
            string.IsNullOrWhiteSpace(row.ListenAddress) ||
            row.ListenPort is < 1 or > 65535)
        {
            return Invalid(row, "CC Switch 供应商或代理字段不兼容。");
        }

        try
        {
            using var settings = JsonDocument.Parse(row.SettingsConfigJson);
            using var meta = JsonDocument.Parse(row.MetaJson);
            var configText = GetString(settings.RootElement, "config");
            if (configText is null)
            {
                return Invalid(row, "CC Switch Codex 设置缺少供应商配置。");
            }

            var config = CodexConfigParser.Parse(configText, includeCredential: false);
            if (!config.IsValid || !config.HasModelProvider)
            {
                return Invalid(row, "CC Switch Codex 供应商配置无效。");
            }

            if (string.Equals(config.ModelProvider, "CodexPlusPlus", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(
                    row,
                    "Codex++ 中转或聚合模式无法安全确定单一上游余额。");
            }

            var rawBaseUrl = config.ActiveSection?.BaseUrl ?? config.TopLevelBaseUrl;
            if (!ActiveProviderDetector.TryNormalizeBaseUri(rawBaseUrl, out var baseUri))
            {
                return Invalid(row, "CC Switch 上游 Base URL 缺失或不安全。");
            }

            var usageScript = GetObject(meta.RootElement, "usage_script") ??
                              GetObject(meta.RootElement, "usageScript");
            var enabled = usageScript is { } script && GetBoolean(script, "enabled") == true;
            var template = usageScript is { } value
                ? GetString(value, "templateType") ?? GetString(value, "template_type")
                : null;
            var knownProvider = ActiveProviderDetector.KnownBalanceTemplate(baseUri!);

            var reason = !enabled
                ? "CC Switch 当前供应商未启用用量查询。"
                : string.Equals(template, "custom", StringComparison.OrdinalIgnoreCase)
                    ? "Balance Capsule 不执行 CC Switch 自定义用量脚本。"
                    : !string.Equals(template, "balance", StringComparison.OrdinalIgnoreCase)
                        ? "当前 CC Switch 用量模板暂不支持。"
                        : baseUri!.Scheme != Uri.UriSchemeHttps
                            ? "CC Switch 上游余额地址必须使用 HTTPS。"
                            : knownProvider is null
                                ? "当前 CC Switch 供应商尚无内置余额适配器。"
                                : null;

            return new CcSwitchSnapshot(
                row.ProviderId,
                string.IsNullOrWhiteSpace(row.DisplayName) ? row.ProviderId : row.DisplayName,
                baseUri,
                row.ListenAddress,
                row.ListenPort,
                reason is null ? knownProvider : template,
                reason);
        }
        catch (JsonException)
        {
            return Invalid(row, "CC Switch 供应商 JSON 结构不兼容。");
        }
    }

    public static string? ReadCredential(CcSwitchDatabaseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            using var settings = JsonDocument.Parse(row.SettingsConfigJson);
            var root = settings.RootElement;
            if (GetObject(root, "auth") is { } auth &&
                GetString(auth, "OPENAI_API_KEY") is { Length: > 0 } apiKey)
            {
                return apiKey;
            }

            var configText = GetString(root, "config");
            return configText is null
                ? null
                : CodexConfigParser.Parse(configText, includeCredential: true)
                    .ReadCredential(_ => null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CcSwitchSnapshot Invalid(CcSwitchDatabaseRow row, string reason) => new(
        string.IsNullOrWhiteSpace(row.ProviderId) ? "unknown" : row.ProviderId,
        string.IsNullOrWhiteSpace(row.DisplayName) ? "CC Switch" : row.DisplayName,
        null,
        row.ListenAddress,
        row.ListenPort,
        null,
        reason);

    private static JsonElement? GetObject(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object
            ? value
            : null;

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
