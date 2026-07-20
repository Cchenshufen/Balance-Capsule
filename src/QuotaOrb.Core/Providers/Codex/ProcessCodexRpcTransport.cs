using System.Diagnostics;

namespace QuotaOrb.Core.Providers.Codex;

public sealed class ProcessCodexRpcTransport : ICodexRpcTransport
{
    private readonly Process _process;
    private readonly Task<string> _stderrDrain;
    private int _disposed;

    private ProcessCodexRpcTransport(Process process)
    {
        _process = process;
        _stderrDrain = process.StandardError.ReadToEndAsync();
    }

    public static ProcessCodexRpcTransport Start(CodexCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var info = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var prefix in command.PrefixArguments)
        {
            info.ArgumentList.Add(prefix);
        }

        foreach (var argument in new[] { "-s", "read-only", "-a", "untrusted", "app-server" })
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("Official Codex CLI could not be started.");
        return new ProcessCodexRpcTransport(process);
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken)
            .ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
    }

    public void Kill()
    {
        if (_disposed != 0)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process has already exited or was never associated with a handle.
        }

        _process.StandardInput.Dispose();

        try
        {
            await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Disposal must not hang on a child process that did not close stderr.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
