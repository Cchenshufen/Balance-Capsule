using Microsoft.Data.Sqlite;

namespace QuotaOrb.Core.Providers.Detection;

public sealed class CcSwitchSqliteDataSource : ICcSwitchDataSource, IClaudeCcSwitchDataSource
{
    private static readonly object ProviderSync = new();
    private static bool _providerInitializationAttempted;
    private static bool _providerAvailable;

    private const string CurrentProviderQuery = """
        SELECT p.id, p.name, p.settings_config, p.meta,
               pc.listen_address, pc.listen_port
        FROM providers AS p
        JOIN proxy_config AS pc ON pc.app_type = $proxy_app_type
        WHERE p.app_type = $app_type AND p.is_current = 1
        LIMIT 1
        """;
    private const string CurrentProviderByIdQuery = """
        SELECT p.id, p.name, p.settings_config, p.meta,
               pc.listen_address, pc.listen_port
        FROM providers AS p
        JOIN proxy_config AS pc ON pc.app_type = $proxy_app_type
        WHERE p.app_type = $app_type AND p.id = $provider_id
        LIMIT 1
        """;

    private readonly string _databasePath;

    public CcSwitchSqliteDataSource(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cc-switch",
            "cc-switch.db");
    }

    public async ValueTask<CcSwitchSnapshot?> ReadCurrentCodexProviderAsync(
        CancellationToken cancellationToken = default)
    {
        var row = await ReadRowAsync("codex", null, cancellationToken).ConfigureAwait(false);
        return row is null ? null : CcSwitchProviderParser.Parse(row);
    }

    public async ValueTask<string?> ReadCurrentCodexCredentialAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var row = await ReadRowAsync("codex", providerId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : CcSwitchProviderParser.ReadCredential(row);
    }

    public async ValueTask<ClaudeCcSwitchSnapshot?> ReadCurrentClaudeProviderAsync(
        string appType,
        CancellationToken cancellationToken = default)
    {
        var row = await ReadRowAsync(appType, null, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ClaudeCcSwitchProviderParser.Parse(row);
    }

    public async ValueTask<string?> ReadCurrentClaudeCredentialAsync(
        string appType,
        string providerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var row = await ReadRowAsync(appType, providerId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : ClaudeCcSwitchProviderParser.ReadCredential(row);
    }

    private async Task<CcSwitchDatabaseRow?> ReadRowAsync(
        string appType,
        string? providerId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            return null;
        }

        if (!EnsureProviderAvailable())
        {
            return null;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 2
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = providerId is null ? CurrentProviderQuery : CurrentProviderByIdQuery;
        command.Parameters.AddWithValue("$app_type", appType);
        command.Parameters.AddWithValue(
            "$proxy_app_type",
            appType == "claude-desktop" ? "claude" : appType);
        if (providerId is not null)
        {
            command.Parameters.AddWithValue("$provider_id", providerId);
        }

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new CcSwitchDatabaseRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5));
    }

    private static bool EnsureProviderAvailable()
    {
        lock (ProviderSync)
        {
            if (_providerInitializationAttempted)
            {
                return _providerAvailable;
            }

            _providerInitializationAttempted = true;
            try
            {
                SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_winsqlite3());
                _providerAvailable = true;
            }
            catch (Exception)
            {
                _providerAvailable = false;
            }

            return _providerAvailable;
        }
    }
}
