using System.Text.Json;
using QuotaOrb.Core.Providers.Codex;

namespace QuotaOrb.Core.Tests.Providers.Codex;

public sealed class CodexRpcClientTests
{
    [Fact]
    public async Task InitializeAndRead_WritesProtocolAndParsesRateLimits()
    {
        var transport = new ScriptedTransport(new[]
        {
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":14,\"windowDurationMins\":300,\"resetsAt\":1783934400},\"secondary\":{\"usedPercent\":38,\"windowDurationMins\":10080,\"resetsAt\":1784366400}}}}"
        });
        await using var client = new CodexRpcClient(transport);

        await client.InitializeAsync();
        var result = await client.ReadRateLimitsAsync();

        Assert.Equal(14, result.RateLimits.Primary?.UsedPercent);
        Assert.Equal(38, result.RateLimits.Secondary?.UsedPercent);
        Assert.Equal(300, result.RateLimits.Primary?.WindowDurationMins);
        Assert.Equal(1784366400, result.RateLimits.Secondary?.ResetsAt);
        Assert.Equal(3, transport.Writes.Count);

        using var initialize = JsonDocument.Parse(transport.Writes[0]);
        Assert.Equal(1, initialize.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        Assert.Equal(
            "balance-capsule-windows",
            initialize.RootElement.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString());

        using var initialized = JsonDocument.Parse(transport.Writes[1]);
        Assert.False(initialized.RootElement.TryGetProperty("id", out _));
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());

        using var read = JsonDocument.Parse(transport.Writes[2]);
        Assert.Equal(2, read.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("account/rateLimits/read", read.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task ReadAccountUsage_ParsesAccountSummaryAndOmitsParams()
    {
        var transport = new ScriptedTransport(new[]
        {
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"result\":{\"summary\":{\"lifetimeTokens\":758444908},\"dailyUsageBuckets\":[{\"startDate\":\"2026-07-20\",\"tokens\":248300}]}}"
        });
        await using var client = new CodexRpcClient(transport);

        await client.InitializeAsync();
        var result = await client.ReadAccountUsageAsync();

        Assert.Equal(758444908, result.Summary?.LifetimeTokens);
        Assert.Equal(248300, Assert.Single(result.DailyUsageBuckets!).Tokens);
        using var request = JsonDocument.Parse(transport.Writes[2]);
        Assert.Equal("account/usage/read", request.RootElement.GetProperty("method").GetString());
        Assert.False(request.RootElement.TryGetProperty("params", out _));
    }

    [Fact]
    public async Task ReadRateLimits_WithRpcError_ThrowsTypedException()
    {
        var transport = new ScriptedTransport(new[]
        {
            "{\"id\":1,\"result\":{}}",
            "{\"id\":2,\"error\":{\"code\":-32601,\"message\":\"Method not found\"}}"
        });
        await using var client = new CodexRpcClient(transport);
        await client.InitializeAsync();

        var error = await Assert.ThrowsAsync<CodexRpcException>(() => client.ReadRateLimitsAsync());

        Assert.Equal(-32601, error.Code);
        Assert.Equal("Method not found", error.Message);
    }

    [Fact]
    public async Task Initialize_WithMalformedJson_ThrowsInvalidData()
    {
        var transport = new ScriptedTransport(new[] { "not-json" });
        await using var client = new CodexRpcClient(transport);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.InitializeAsync());
    }

    [Fact]
    public async Task Initialize_WithClosedStdout_ThrowsEndOfStream()
    {
        var transport = new ScriptedTransport(new string?[] { null });
        await using var client = new CodexRpcClient(transport);

        await Assert.ThrowsAsync<EndOfStreamException>(() => client.InitializeAsync());
    }

    [Fact]
    public async Task Initialize_WhenRequestTimesOut_KillsTransport()
    {
        var transport = new ScriptedTransport(Array.Empty<string?>(), waitWhenEmpty: true);
        await using var client = new CodexRpcClient(transport, TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<TimeoutException>(() => client.InitializeAsync());

        Assert.True(transport.KillCalled);
    }

    [Fact]
    public async Task Requests_IgnoreNotificationsAndResponsesForOtherIds()
    {
        var transport = new ScriptedTransport(new[]
        {
            "{\"method\":\"account/updated\",\"params\":{}}",
            "{\"id\":99,\"result\":{}}",
            "{\"id\":1,\"result\":{}}",
            "{\"method\":\"account/updated\",\"params\":{}}",
            "{\"id\":2,\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":7,\"windowDurationMins\":300,\"resetsAt\":1783934400},\"secondary\":null}}}"
        });
        await using var client = new CodexRpcClient(transport);

        await client.InitializeAsync();
        var result = await client.ReadRateLimitsAsync();

        Assert.Equal(7, result.RateLimits.Primary?.UsedPercent);
    }
}
