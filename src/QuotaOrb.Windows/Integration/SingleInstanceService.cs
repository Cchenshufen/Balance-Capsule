namespace QuotaOrb.Windows.Integration;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\QuotaOrb.SingleInstance";
    private Mutex? _mutex;
    private bool _ownsMutex;

    public bool TryAcquire()
    {
        if (_mutex is not null)
        {
            return _ownsMutex;
        }

        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            _mutex = mutex;
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                try
                {
                    _ownsMutex = mutex.WaitOne(TimeSpan.Zero);
                }
                catch (AbandonedMutexException)
                {
                    _ownsMutex = true;
                }
            }

            if (!_ownsMutex)
            {
                mutex.Dispose();
                _mutex = null;
            }

            return _ownsMutex;
        }
        catch (UnauthorizedAccessException)
        {
            _mutex?.Dispose();
            _mutex = null;
            _ownsMutex = false;
            return false;
        }
    }

    public void Dispose()
    {
        if (_mutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }
}
