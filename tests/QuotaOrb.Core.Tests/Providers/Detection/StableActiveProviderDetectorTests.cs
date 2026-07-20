using QuotaOrb.Core.Providers.Detection;

namespace QuotaOrb.Core.Tests.Providers.Detection;

public sealed class StableActiveProviderDetectorTests
{
    private static readonly ActiveProvider ThirdParty = new(
        ActiveProviderMode.CcSwitch,
        "provider-1",
        "DeepSeek",
        new Uri("https://api.deepseek.com"),
        "deepseek",
        null);

    private static readonly ActiveProvider Official = new(
        ActiveProviderMode.Official,
        "openai",
        "OpenAI",
        null,
        null,
        null);

    [Fact]
    public async Task DetectAsync_SameAgentProcessPinsLastThirdPartySource()
    {
        IReadOnlyCollection<int> processIds = [42];
        var inner = new SequenceDetector(ThirdParty, Official);
        var detector = new StableActiveProviderDetector(inner, () => processIds);

        var first = await detector.DetectAsync();
        var second = await detector.DetectAsync();
        var credential = await detector.ReadCredentialAsync(second);

        Assert.Equal(ThirdParty, first);
        Assert.Equal(ThirdParty.Fingerprint, second.Fingerprint);
        Assert.True(second.IsPinnedToRunningAgent);
        Assert.Equal("secret", credential);
    }

    [Fact]
    public async Task DetectAsync_ReplacedAgentProcessAcceptsCurrentSource()
    {
        IReadOnlyCollection<int> processIds = [42];
        var inner = new SequenceDetector(ThirdParty, Official);
        var detector = new StableActiveProviderDetector(inner, () => processIds);

        await detector.DetectAsync();
        processIds = [84];
        var result = await detector.DetectAsync();

        Assert.Equal(ActiveProviderMode.Official, result.Mode);
        Assert.False(result.IsPinnedToRunningAgent);
    }

    private sealed class SequenceDetector(params ActiveProvider[] providers)
        : IActiveProviderDetector
    {
        private readonly Queue<ActiveProvider> _providers = new(providers);

        public ValueTask<ActiveProvider> DetectAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_providers.Dequeue());

        public ValueTask<string?> ReadCredentialAsync(
            ActiveProvider provider,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>("secret");
    }
}
