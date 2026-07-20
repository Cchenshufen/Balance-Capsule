using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers.Balance;

public interface IBalanceProvider
{
    Task<BalanceSnapshot> ReadAsync(
        Uri baseUri,
        string credential,
        CancellationToken cancellationToken);
}
