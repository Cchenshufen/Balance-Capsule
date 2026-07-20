using QuotaOrb.Core.Domain;

namespace QuotaOrb.Core.Providers;

public interface IQuotaProvider
{
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken);
}
