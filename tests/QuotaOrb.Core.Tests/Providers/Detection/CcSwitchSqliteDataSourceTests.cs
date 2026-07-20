using Microsoft.Data.Sqlite;
using QuotaOrb.Core.Providers.Detection;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Providers.Detection;

public sealed class CcSwitchSqliteDataSourceTests
{
    [Fact]
    public async Task ReadCurrentProvider_UsesReadOnlyDatabaseAndReturnsCredentialOnDemand()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.CreateFile("cc-switch.db");
        var source = new CcSwitchSqliteDataSource(databasePath);
        await CreateDatabaseAsync(databasePath);

        var provider = await source.ReadCurrentCodexProviderAsync();
        var credential = await source.ReadCurrentCodexCredentialAsync("provider-1");

        Assert.NotNull(provider);
        Assert.Equal("provider-1", provider.ProviderId);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.Equal("https://api.deepseek.com/v1", provider.UpstreamBaseUri!.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("fixture-secret", credential);
        Assert.DoesNotContain("fixture-secret", provider.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCredential_RejectsNonCurrentProviderId()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.CreateFile("cc-switch.db");
        var source = new CcSwitchSqliteDataSource(databasePath);
        await CreateDatabaseAsync(databasePath);

        var credential = await source.ReadCurrentCodexCredentialAsync("different-provider");

        Assert.Null(credential);
    }

    [Fact]
    public async Task ReadCredential_AllowsPreviouslyConfirmedInactiveProviderId()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.CreateFile("cc-switch.db");
        var source = new CcSwitchSqliteDataSource(databasePath);
        await CreateDatabaseAsync(databasePath);

        var credential = await source.ReadCurrentCodexCredentialAsync("provider-old");

        Assert.Equal("old-secret", credential);
    }

    [Fact]
    public async Task ReadCurrentClaudeProvider_UsesRequestedDesktopAppType()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = directory.CreateFile("cc-switch.db");
        var source = new CcSwitchSqliteDataSource(databasePath);
        await CreateDatabaseAsync(databasePath);

        var provider = await source.ReadCurrentClaudeProviderAsync("claude-desktop");
        var credential = await source.ReadCurrentClaudeCredentialAsync(
            "claude-desktop",
            "desktop-provider");

        Assert.NotNull(provider);
        Assert.Equal("desktop-provider", provider.ProviderId);
        Assert.Equal("deepseek", provider.BalanceTemplate);
        Assert.Equal("desktop-secret", credential);
    }

    private static async Task CreateDatabaseAsync(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE providers (
                id TEXT NOT NULL,
                app_type TEXT NOT NULL,
                name TEXT NOT NULL,
                settings_config TEXT NOT NULL,
                meta TEXT NOT NULL,
                is_current BOOLEAN NOT NULL DEFAULT 0,
                PRIMARY KEY (id, app_type));
            CREATE TABLE proxy_config (
                app_type TEXT PRIMARY KEY,
                listen_address TEXT NOT NULL,
                listen_port INTEGER NOT NULL);
            INSERT INTO providers (id, app_type, name, settings_config, meta, is_current)
            VALUES (
                'provider-1',
                'codex',
                'DeepSeek',
                '{"auth":{"OPENAI_API_KEY":"fixture-secret"},"config":"model_provider = \"deepseek\"\n[model_providers.deepseek]\nbase_url = \"https://api.deepseek.com/v1\"\n"}',
                '{"usage_script":{"enabled":true,"templateType":"balance"}}',
                1);
            INSERT INTO proxy_config (app_type, listen_address, listen_port)
            VALUES ('codex', '127.0.0.1', 15721);
            INSERT INTO providers (id, app_type, name, settings_config, meta, is_current)
            VALUES (
                'provider-old',
                'codex',
                'Old DeepSeek',
                '{"auth":{"OPENAI_API_KEY":"old-secret"},"config":"model_provider = \"deepseek\"\n[model_providers.deepseek]\nbase_url = \"https://api.deepseek.com/v1\"\n"}',
                '{}',
                0);
            INSERT INTO providers (id, app_type, name, settings_config, meta, is_current)
            VALUES (
                'desktop-provider',
                'claude-desktop',
                'DeepSeek',
                '{"env":{"ANTHROPIC_BASE_URL":"https://api.deepseek.com/anthropic","ANTHROPIC_AUTH_TOKEN":"desktop-secret"}}',
                '{}',
                1);
            INSERT INTO proxy_config (app_type, listen_address, listen_port)
            VALUES ('claude', '127.0.0.1', 15721);
            """;
        await command.ExecuteNonQueryAsync();
    }
}
