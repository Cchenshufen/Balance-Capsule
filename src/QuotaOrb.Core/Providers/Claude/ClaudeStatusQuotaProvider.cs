using System.Text.Json;
using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers.Claude;

public sealed class ClaudeStatusQuotaProvider : IQuotaProvider
{
    private const int MaxCacheBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly string _cachePath;
    private readonly TimeProvider _timeProvider;

    public ClaudeStatusQuotaProvider(
        string? cachePath = null,
        TimeProvider? timeProvider = null)
    {
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuotaOrb",
            "claude-status.json");
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
        {
            throw Error(
                "status-unavailable",
                "Claude Desktop 不会同步 statusLine；请在桌面模型旁查看用量环，或在终端启动 Claude Code CLI 并发送一条消息。");
        }

        var file = new FileInfo(_cachePath);
        if (file.Length is <= 0 or > MaxCacheBytes)
        {
            throw Error("invalid-status", "Claude Code 额度缓存无效。");
        }

        try
        {
            await using var stream = new FileStream(
                _cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                useAsync: true);
            var cache = await JsonSerializer.DeserializeAsync<ClaudeStatusCache>(
                    stream,
                    SerializerOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (cache is null)
            {
                throw Error("invalid-status", "Claude Code 额度缓存无效。");
            }

            var now = _timeProvider.GetUtcNow();
            var current = MapWindow(cache.FiveHour, TimeSpan.FromHours(5), now);
            var weekly = MapWindow(cache.SevenDay, TimeSpan.FromDays(7), now);
            if (current is null && weekly is null)
            {
                throw Error("stale-status", "Claude Code 额度缓存已过期，请发送一条消息刷新。");
            }

            return new QuotaSnapshot(current, weekly, cache.CapturedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (QuotaProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error("invalid-status", "Claude Code 额度缓存无效。", exception);
        }
        catch (IOException exception)
        {
            throw Error("status-read-failed", "无法读取 Claude Code 额度缓存。", exception);
        }
    }

    private static QuotaWindow? MapWindow(
        ClaudeStatusWindow? window,
        TimeSpan duration,
        DateTimeOffset now) =>
        window is null || window.ResetsAt <= now
            ? null
            : new QuotaWindow(
                Math.Clamp(window.UsedPercentage, 0d, 100d),
                duration,
                window.ResetsAt);

    private static QuotaProviderException Error(
        string code,
        string message,
        Exception? inner = null) => new(code, message, inner);

    public sealed record ClaudeStatusCache(
        DateTimeOffset CapturedAt,
        ClaudeStatusWindow? FiveHour,
        ClaudeStatusWindow? SevenDay);

    public sealed record ClaudeStatusWindow(double UsedPercentage, DateTimeOffset ResetsAt);
}
