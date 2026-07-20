using QuotaOrb.Core.Providers.Codex;

namespace QuotaOrb.Core.Tests.Providers.Codex;

internal sealed class ScriptedTransport : ICodexRpcTransport
{
    private readonly Queue<string?> _replies;
    private readonly bool _waitWhenEmpty;

    public ScriptedTransport(IEnumerable<string?> replies, bool waitWhenEmpty = false)
    {
        _replies = new Queue<string?>(replies);
        _waitWhenEmpty = waitWhenEmpty;
    }

    public List<string> Writes { get; } = new();

    public bool KillCalled { get; private set; }

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Writes.Add(line);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_replies.Count > 0)
        {
            return _replies.Dequeue();
        }

        if (!_waitWhenEmpty)
        {
            return null;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }

    public void Kill() => KillCalled = true;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
