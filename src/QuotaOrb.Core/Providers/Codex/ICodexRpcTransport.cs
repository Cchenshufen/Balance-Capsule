namespace QuotaOrb.Core.Providers.Codex;

public interface ICodexRpcTransport : IAsyncDisposable
{
    Task WriteLineAsync(string line, CancellationToken cancellationToken);

    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    void Kill();
}
