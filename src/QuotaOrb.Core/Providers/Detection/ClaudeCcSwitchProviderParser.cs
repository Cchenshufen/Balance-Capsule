using System.Text.Json;

namespace QuotaOrb.Core.Providers.Detection;

public interface IClaudeCcSwitchDataSource
{
    ValueTask<ClaudeCcSwitchSnapshot?> ReadCurrentClaudeProviderAsync(
        string appType,
        CancellationToken cancellationToken = default);

    ValueTask<string?> ReadCurrentClaudeCredentialAsync(
        string appType,
        string providerId,
        CancellationToken cancellationToken = default);
}

public sealed record ClaudeCcSwitchSnapshot(
    string ProviderId,
    string DisplayName,
    Uri? UpstreamBaseUri,
    string ListenAddress,
    int ListenPort,
    string? BalanceTemplate,
    string? SupportReason,
    bool IsOfficial)
{
    public bool MatchesProxy(Uri baseUri) =>
        baseUri.IsLoopback && baseUri.Port == ListenPort;
}

public static class ClaudeCcSwitchProviderParser
{
    public static ClaudeCcSwitchSnapshot Parse(CcSwitchDatabaseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (string.IsNullOrWhiteSpace(row.ProviderId) ||
            string.IsNullOrWhiteSpace(row.ListenAddress) ||
            row.ListenPort is < 1 or > 65535)
        {
            return Invalid(row, "CC Switch Claude 供应商或代理字段不兼容。");
        }

        try
        {
            using var settings = JsonDocument.Parse(row.SettingsConfigJson);
            var env = GetObject(settings.RootElement, "env");
            var rawBaseUrl = env is { } value
                ? GetString(value, "ANTHROPIC_BASE_URL")
                : null;
            if (string.IsNullOrWhiteSpace(rawBaseUrl))
            {
                return new ClaudeCcSwitchSnapshot(
                    row.ProviderId,
                    DisplayName(row),
                    null,
                    row.ListenAddress,
                    row.ListenPort,
                    null,
                    null,
                    IsOfficial: true);
            }

            if (!ActiveProviderDetector.TryNormalizeBaseUri(rawBaseUrl, out var baseUri) ||
                baseUri!.Scheme != Uri.UriSchemeHttps)
            {
                return Invalid(row, "CC Switch Claude 上游 Base URL 缺失或不安全。");
            }

            var template = ActiveProviderDetector.KnownBalanceTemplate(baseUri);
            return new ClaudeCcSwitchSnapshot(
                row.ProviderId,
                DisplayName(row),
                baseUri,
                row.ListenAddress,
                row.ListenPort,
                template,
                template is null ? "当前 Claude 供应商尚无内置余额适配器。" : null,
                IsOfficial: false);
        }
        catch (JsonException)
        {
            return Invalid(row, "CC Switch Claude 供应商 JSON 结构不兼容。");
        }
    }

    public static string? ReadCredential(CcSwitchDatabaseRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            using var settings = JsonDocument.Parse(row.SettingsConfigJson);
            if (GetObject(settings.RootElement, "env") is not { } env)
            {
                return null;
            }

            return GetString(env, "ANTHROPIC_AUTH_TOKEN") ??
                   GetString(env, "ANTHROPIC_API_KEY");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeCcSwitchSnapshot Invalid(CcSwitchDatabaseRow row, string reason) => new(
        string.IsNullOrWhiteSpace(row.ProviderId) ? "unknown" : row.ProviderId,
        DisplayName(row),
        null,
        row.ListenAddress,
        row.ListenPort,
        null,
        reason,
        IsOfficial: false);

    private static string DisplayName(CcSwitchDatabaseRow row) =>
        string.IsNullOrWhiteSpace(row.DisplayName) ? row.ProviderId : row.DisplayName;

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
}
