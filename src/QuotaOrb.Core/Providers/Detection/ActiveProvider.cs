namespace QuotaOrb.Core.Providers.Detection;

public enum ActiveProviderMode
{
    Official,
    Direct,
    CcSwitch,
    CodexPlusPlus,
    Unsupported
}

public sealed record ActiveProvider(
    ActiveProviderMode Mode,
    string ProviderId,
    string DisplayName,
    Uri? BaseUri,
    string? BalanceTemplate,
    string? SupportReason)
{
    public bool IsSupported => SupportReason is null;

    public bool IsPinnedToRunningAgent { get; init; }

    public string Fingerprint => string.Join(
        '|',
        Mode,
        ProviderId,
        BaseUri?.AbsoluteUri ?? "-",
        BalanceTemplate ?? "-");
}
