using QuotaOrb.Core.Providers.Detection;

namespace QuotaOrb.Core.Tests.Providers.Detection;

public sealed class CcSwitchProviderParserTests
{
    [Fact]
    public void Parse_ReadsCurrentProviderProxyAndBuiltInBalanceTemplate()
    {
        const string secret = "sk-cc-switch-secret";
        var row = CreateRow(
            """
            {
              "auth": { "OPENAI_API_KEY": "sk-cc-switch-secret" },
              "config": "model_provider = \"deepseek\"\n[model_providers.deepseek]\nbase_url = \"https://api.deepseek.com/v1\"\n"
            }
            """,
            """
            {
              "usage_script": {
                "enabled": true,
                "language": "javascript",
                "code": "throw new Error('must never execute')",
                "templateType": "balance"
              }
            }
            """);

        var result = CcSwitchProviderParser.Parse(row);

        Assert.Equal("provider-1", result.ProviderId);
        Assert.Equal("DeepSeek", result.DisplayName);
        Assert.Equal(15721, result.ListenPort);
        Assert.Equal("deepseek", result.BalanceTemplate);
        Assert.Equal("https://api.deepseek.com/v1", result.UpstreamBaseUri!.AbsoluteUri.TrimEnd('/'));
        Assert.Null(result.SupportReason);
        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("must never execute", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CustomScriptFailsClosed()
    {
        var row = CreateRow(
            SettingsConfig,
            """
            {
              "usage_script": {
                "enabled": true,
                "language": "javascript",
                "code": "malicious()",
                "templateType": "custom"
              }
            }
            """);

        var result = CcSwitchProviderParser.Parse(row);

        Assert.NotNull(result.SupportReason);
        Assert.Equal("custom", result.BalanceTemplate);
    }

    [Fact]
    public void Parse_AcceptsLegacySnakeCaseTemplateField()
    {
        var result = CcSwitchProviderParser.Parse(CreateRow(
            SettingsConfig,
            """{"usage_script":{"enabled":true,"template_type":"balance"}}"""));

        Assert.Null(result.SupportReason);
        Assert.Equal("deepseek", result.BalanceTemplate);
    }

    [Fact]
    public void Parse_DisabledOrInvalidMetadataFailsClosed()
    {
        var disabled = CcSwitchProviderParser.Parse(CreateRow(
            SettingsConfig,
            """{"usage_script":{"enabled":false,"templateType":"balance"}}"""));
        var invalid = CcSwitchProviderParser.Parse(CreateRow(SettingsConfig, "not-json"));

        Assert.NotNull(disabled.SupportReason);
        Assert.NotNull(invalid.SupportReason);
        Assert.Null(invalid.UpstreamBaseUri);
    }

    [Fact]
    public void Parse_CodexPlusPlusBehindCcSwitchFailsClosed()
    {
        var result = CcSwitchProviderParser.Parse(CreateRow(
            """
            {
              "auth": { "OPENAI_API_KEY": "relay-secret" },
              "config": "model_provider = \"CodexPlusPlus\"\n[model_providers.CodexPlusPlus]\nbase_url = \"https://api.deepseek.com\"\n"
            }
            """,
            """{"usage_script":{"enabled":true,"templateType":"balance"}}"""));

        Assert.NotNull(result.SupportReason);
        Assert.Null(result.UpstreamBaseUri);
        Assert.DoesNotContain("relay-secret", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadCredential_ReturnsSecretOnlyFromExplicitCall()
    {
        var row = CreateRow(
            """
            {
              "auth": { "OPENAI_API_KEY": "temporary-secret" },
              "config": "model_provider = \"deepseek\"\n[model_providers.deepseek]\nbase_url = \"https://api.deepseek.com\"\n"
            }
            """,
            """{"usage_script":{"enabled":true,"templateType":"balance"}}""");

        var snapshot = CcSwitchProviderParser.Parse(row);
        var credential = CcSwitchProviderParser.ReadCredential(row);

        Assert.Equal("temporary-secret", credential);
        Assert.DoesNotContain("temporary-secret", snapshot.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentProviderQueryIsReadOnlyAndCapabilityScoped()
    {
        var query = CcSwitchProviderParser.CurrentCodexProviderQuery;

        Assert.StartsWith("SELECT", query.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p.app_type = 'codex'", query, StringComparison.Ordinal);
        Assert.Contains("p.is_current = 1", query, StringComparison.Ordinal);
        Assert.Contains("pc.listen_port", query, StringComparison.Ordinal);
    }

    private const string SettingsConfig = """
        {
          "auth": { "OPENAI_API_KEY": "fixture-secret" },
          "config": "model_provider = \"deepseek\"\n[model_providers.deepseek]\nbase_url = \"https://api.deepseek.com\"\n"
        }
        """;

    private static CcSwitchDatabaseRow CreateRow(string settings, string meta) => new(
        "provider-1",
        "DeepSeek",
        settings,
        meta,
        "127.0.0.1",
        15721);
}
