namespace QuotaOrb.Core.Providers.Detection;

public interface IActiveProviderDetector
{
    ValueTask<ActiveProvider> DetectAsync(CancellationToken cancellationToken = default);

    ValueTask<string?> ReadCredentialAsync(
        ActiveProvider provider,
        CancellationToken cancellationToken = default);
}
