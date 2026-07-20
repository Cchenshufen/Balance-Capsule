namespace QuotaOrb.Core.Providers;

public sealed class QuotaProviderException : Exception
{
    public QuotaProviderException(string code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public string Code { get; }
}
