using System.Text.Json;

namespace QuotaOrb.Core.Providers.Codex;

public sealed class CodexRpcClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICodexRpcTransport _transport;
    private readonly TimeSpan _requestTimeout;
    private int _nextId;
    private bool _initialized;

    public CodexRpcClient(
        ICodexRpcTransport transport,
        TimeSpan? requestTimeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);

        if (_requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "The request timeout must be positive.");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "balance-capsule-windows",
                        version = "BalanceCapsule-win.15"
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        var notification = JsonSerializer.Serialize(new
        {
            method = "initialized",
            @params = new { }
        });
        await _transport.WriteLineAsync(notification, cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<RpcRateLimitsResponse> ReadRateLimitsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The Codex RPC client must be initialized first.");
        }

        var result = await SendRequestAsync(
                "account/rateLimits/read",
                new { },
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var response = result.Deserialize<RpcRateLimitsResponse>(SerializerOptions);
            if (response?.RateLimits is null)
            {
                throw new InvalidDataException("Codex returned an invalid rate-limit response.");
            }

            return response;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Codex returned an invalid rate-limit response.", exception);
        }
    }

    public async Task<RpcAccountUsageResponse> ReadAccountUsageAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The Codex RPC client must be initialized first.");
        }

        var result = await SendRequestAsync(
                "account/usage/read",
                null,
                cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return result.Deserialize<RpcAccountUsageResponse>(SerializerOptions)
                ?? throw new InvalidDataException("Codex returned an invalid account-usage response.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Codex returned an invalid account-usage response.", exception);
        }
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    private async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = parameters is null
            ? JsonSerializer.Serialize(new { id, method })
            : JsonSerializer.Serialize(new { id, method, @params = parameters });

        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            await _transport.WriteLineAsync(request, linked.Token).ConfigureAwait(false);

            while (true)
            {
                var line = await _transport.ReadLineAsync(linked.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw new EndOfStreamException("Codex app-server closed its output stream.");
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException("Codex app-server returned malformed JSON.", exception);
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var responseId) ||
                        responseId.ValueKind != JsonValueKind.Number ||
                        !responseId.TryGetInt32(out var parsedId) ||
                        parsedId != id)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error))
                    {
                        var code = error.TryGetProperty("code", out var codeElement) &&
                                   codeElement.TryGetInt32(out var parsedCode)
                            ? parsedCode
                            : 0;
                        var message = error.TryGetProperty("message", out var messageElement)
                            ? messageElement.GetString() ?? "Codex app-server returned an RPC error."
                            : "Codex app-server returned an RPC error.";
                        throw new CodexRpcException(code, message);
                    }

                    if (!root.TryGetProperty("result", out var result))
                    {
                        throw new InvalidDataException("Codex app-server response contained no result.");
                    }

                    return result.Clone();
                }
            }
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            _transport.Kill();
            throw new TimeoutException("Codex app-server request timed out.", exception);
        }
    }
}
