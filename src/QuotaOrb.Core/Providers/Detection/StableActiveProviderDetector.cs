namespace QuotaOrb.Core.Providers.Detection;

public sealed class StableActiveProviderDetector(
    IActiveProviderDetector inner,
    Func<IReadOnlyCollection<int>> readAgentProcessIds) : IActiveProviderDetector
{
    private ActiveProvider? _lastThirdParty;
    private HashSet<int> _anchorProcessIds = [];

    public async ValueTask<ActiveProvider> DetectAsync(
        CancellationToken cancellationToken = default)
    {
        var detected = await inner.DetectAsync(cancellationToken).ConfigureAwait(false);
        var processIds = ReadProcessIds();

        if (detected.IsSupported && detected.Mode is
            ActiveProviderMode.Direct or
            ActiveProviderMode.CcSwitch or
            ActiveProviderMode.CodexPlusPlus)
        {
            _lastThirdParty = detected;
            _anchorProcessIds = processIds;
            return detected;
        }

        if (_lastThirdParty is not null && _anchorProcessIds.Overlaps(processIds))
        {
            return _lastThirdParty with { IsPinnedToRunningAgent = true };
        }

        _lastThirdParty = null;
        _anchorProcessIds.Clear();
        return detected;
    }

    public ValueTask<string?> ReadCredentialAsync(
        ActiveProvider provider,
        CancellationToken cancellationToken = default) =>
        inner.ReadCredentialAsync(provider, cancellationToken);

    private HashSet<int> ReadProcessIds()
    {
        try
        {
            return readAgentProcessIds().Where(id => id > 0).ToHashSet();
        }
        catch
        {
            return [];
        }
    }
}
