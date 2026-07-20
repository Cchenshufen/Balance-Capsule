using System.Text.Json;

namespace QuotaOrb.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly string _settingsPath;
    private readonly string _temporaryPath;
    private readonly string _invalidPath;

    public JsonSettingsStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaOrb");
        _settingsPath = Path.Combine(_root, "settings.json");
        _temporaryPath = Path.Combine(_root, "settings.tmp");
        _invalidPath = Path.Combine(_root, "settings.invalid.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return settings ?? throw new JsonException("Settings JSON cannot be null.");
        }
        catch (JsonException)
        {
            QuarantineInvalidSettings();
            return new AppSettings();
        }
        catch (NotSupportedException)
        {
            QuarantineInvalidSettings();
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(_root);

        try
        {
            await using (var stream = new FileStream(
                             _temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(_temporaryPath, _settingsPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(_temporaryPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(_temporaryPath))
            {
                File.Delete(_temporaryPath);
            }
        }
    }

    private void QuarantineInvalidSettings()
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.Move(_settingsPath, _invalidPath, overwrite: true);
        }
        catch (IOException)
        {
            // Defaults remain usable even if a locked invalid file cannot be moved.
        }
        catch (UnauthorizedAccessException)
        {
            // Defaults remain usable even if the settings directory is read-only.
        }
    }
}
