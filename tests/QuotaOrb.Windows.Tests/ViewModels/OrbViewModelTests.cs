using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Settings;
using QuotaOrb.Windows.ViewModels;

namespace QuotaOrb.Windows.Tests.ViewModels;

public sealed class OrbViewModelTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-13T12:00:00+08:00");

    [Fact]
    public void Apply_WithSafeState_MapsTextLiquidAndPalette()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            62,
            QuotaRisk.Safe,
            new QuotaWindow(14, TimeSpan.FromHours(5), Now.AddHours(4)),
            new QuotaWindow(38, TimeSpan.FromDays(7), Now.AddDays(2)),
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal("86", viewModel.DisplayText);
        Assert.Equal("5h 剩余", viewModel.CaptionText);
        Assert.Equal(26d, viewModel.CompactDisplayFontSize);
        Assert.Equal(40d, viewModel.DetailDisplayFontSize);
        Assert.Equal(58.48, viewModel.LiquidHeight, precision: 2);
        Assert.Equal("Safe", viewModel.PaletteKey);
        Assert.Equal("4小时后", viewModel.ResetValue);
        Assert.Equal("5h 限额", viewModel.CurrentLabel);
        Assert.Equal("86% 剩余", viewModel.CurrentValue);
        Assert.Equal("一周限额", viewModel.WeeklyLabel);
        Assert.Equal("62% 剩余", viewModel.WeeklyValue);
        Assert.Equal("5H", viewModel.CurrentMetricTag);
        Assert.Equal("7D", viewModel.WeeklyMetricTag);
    }

    [Fact]
    public void Apply_WithSevenDaySafeState_MapsRichMetadata()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            81,
            QuotaRisk.Safe,
            new QuotaWindow(19, TimeSpan.FromDays(7), Now.AddDays(6).AddHours(18)),
            null,
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal(81d, viewModel.RemainingPercent);
        Assert.Equal("一周剩余", viewModel.CaptionText);
        Assert.False(viewModel.HasFiveHourQuota);
        Assert.True(viewModel.HasWeeklyQuota);
        Assert.Equal(QuotaWindowMode.Weekly, viewModel.EffectiveQuotaWindow);
        Assert.Equal(0.81d, viewModel.RemainingRatio, precision: 2);
        Assert.Equal("状态良好", viewModel.StatusText);
        Assert.Equal("暂不可用", viewModel.CurrentValue);
        Assert.Equal("81% 剩余", viewModel.WeeklyValue);
        Assert.True(viewModel.IsDataAvailable);
    }

    [Fact]
    public void Apply_WithWeeklySelected_ShowsWeeklyWindow()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        viewModel.ApplySettings(new AppSettings { QuotaWindow = QuotaWindowMode.Weekly });
        var state = new QuotaViewState(
            62,
            QuotaRisk.Safe,
            new QuotaWindow(14, TimeSpan.FromHours(5), Now.AddHours(4)),
            new QuotaWindow(38, TimeSpan.FromDays(7), Now.AddDays(2)),
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal("62", viewModel.DisplayText);
        Assert.Equal("一周剩余", viewModel.CaptionText);
        Assert.Equal(42.16, viewModel.LiquidHeight, precision: 2);
    }

    [Fact]
    public void Apply_WithError_ClearsRichMetadataInsteadOfLeakingOldQuota()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        viewModel.Apply(new QuotaViewState(
            81,
            QuotaRisk.Safe,
            new QuotaWindow(19, TimeSpan.FromDays(7), Now.AddDays(6)),
            null,
            Now,
            null));

        viewModel.Apply(new QuotaViewState(
            null,
            QuotaRisk.Error,
            null,
            null,
            Now.AddMinutes(1),
            new QuotaReadError("timeout", "Codex request timed out.")));

        Assert.Equal(0d, viewModel.RemainingPercent);
        Assert.Equal(0d, viewModel.RemainingRatio);
        Assert.Equal("读取失败", viewModel.StatusText);
        Assert.False(viewModel.IsDataAvailable);
    }

    [Fact]
    public void Apply_WithError_ShowsErrorMarkerAndSafeMessage()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var error = new QuotaReadError("timeout", "Codex request timed out.");
        var state = new QuotaViewState(
            null,
            QuotaRisk.Error,
            null,
            null,
            Now,
            error);

        viewModel.Apply(state);

        Assert.Equal("!", viewModel.DisplayText);
        Assert.Equal("Error", viewModel.PaletteKey);
        Assert.Equal(error.Message, viewModel.ErrorMessage);
    }

    [Fact]
    public void Apply_WithStaleQuota_KeepsValueAndOriginalPalette()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var error = new QuotaReadError("timeout", "Codex request timed out.");
        var state = new QuotaViewState(
            62,
            QuotaRisk.Safe,
            new QuotaWindow(38, TimeSpan.FromHours(5), Now.AddHours(4)),
            null,
            Now.AddMinutes(-1),
            error)
        {
            IsStale = true
        };

        viewModel.Apply(state);

        Assert.Equal("62", viewModel.DisplayText);
        Assert.Equal("数据已过期", viewModel.StatusText);
        Assert.Equal("Safe", viewModel.PaletteKey);
        Assert.True(viewModel.IsDataAvailable);
        Assert.True(viewModel.IsStale);
        Assert.False(viewModel.HasError);
        Assert.Equal(error.Message, viewModel.ErrorMessage);
    }

    [Fact]
    public void Apply_WithMissingWeeklyWindow_ShowsUnavailable()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            73,
            QuotaRisk.Safe,
            new QuotaWindow(27, TimeSpan.FromHours(5), Now.AddHours(4)),
            null,
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal("暂不可用", viewModel.WeeklyValue);
    }

    [Fact]
    public void Apply_WithDeepSeekBalance_ShowsAccountBalanceWithoutFakePercentRisk()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            Now,
            null)
        {
            Balance = new BalanceSnapshot(
                "deepseek",
                "DeepSeek",
                BalanceKind.AccountBalance,
                new[]
                {
                    new BalanceAmount(28m, "CNY"),
                    new BalanceAmount(1.25m, "USD")
                },
                Now),
            SourceName = "CC Switch · DeepSeek"
        };

        viewModel.Apply(state);

        Assert.Equal("¥28.00", viewModel.DisplayText);
        Assert.Equal(18d, viewModel.CompactDisplayFontSize);
        Assert.Equal(28d, viewModel.DetailDisplayFontSize);
        Assert.Equal("账户余额", viewModel.CaptionText);
        Assert.Equal("CC Switch · DeepSeek", viewModel.SourceName);
        Assert.Equal("当前余额", viewModel.CurrentLabel);
        Assert.Equal("¥28.00 · $1.25", viewModel.CurrentValue);
        Assert.Equal("当前供应商", viewModel.WeeklyLabel);
        Assert.Equal("DeepSeek", viewModel.WeeklyValue);
        Assert.Equal("Safe", viewModel.PaletteKey);
        Assert.Equal(34d, viewModel.LiquidHeight);
        Assert.Equal(0d, viewModel.RemainingPercent);
        Assert.False(viewModel.ShowsPercent);
        Assert.False(viewModel.HasFiveHourQuota);
        Assert.False(viewModel.HasWeeklyQuota);
        Assert.True(viewModel.IsDataAvailable);
    }

    [Fact]
    public void Apply_WithOpenRouterBalance_LabelsCurrentKeyLimit()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            Now,
            null)
        {
            Balance = new BalanceSnapshot(
                "openrouter",
                "OpenRouter",
                BalanceKind.ApiKeyLimitRemaining,
                new[] { new BalanceAmount(12.34m, "USD") },
                Now)
        };

        viewModel.Apply(state);

        Assert.Equal("$12.34", viewModel.DisplayText);
        Assert.Equal("密钥剩余额度", viewModel.CaptionText);
        Assert.Equal("OpenRouter", viewModel.SourceName);
        Assert.Equal("当前余额", viewModel.CurrentLabel);
        Assert.Equal("$12.34", viewModel.CurrentValue);
        Assert.Equal("当前供应商", viewModel.WeeklyLabel);
        Assert.Equal("OpenRouter", viewModel.WeeklyValue);
        Assert.Equal("额度", viewModel.CurrentMetricTag);
        Assert.Equal("API", viewModel.WeeklyMetricTag);
        Assert.False(viewModel.ShowsPercent);
    }

    [Theory]
    [InlineData(AgentSource.Codex, "Codex")]
    [InlineData(AgentSource.Claude, "Claude")]
    [InlineData(AgentSource.ClaudeCode, "Claude Code")]
    public void SetSelectedAgent_MapsCurrentSoftware(
        AgentSource source,
        string expected)
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));

        viewModel.SetSelectedAgent(source);

        Assert.Equal(expected, viewModel.AgentName);
    }

    [Fact]
    public void Apply_WithOpenRouterUnlimitedKey_ShowsNoConfiguredLimit()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            Now,
            null)
        {
            Balance = new BalanceSnapshot(
                "openrouter",
                "OpenRouter",
                BalanceKind.ApiKeyLimitRemaining,
                Array.Empty<BalanceAmount>(),
                Now,
                "API key has no configured limit.")
        };

        viewModel.Apply(state);

        Assert.Equal("—", viewModel.DisplayText);
        Assert.Equal("未设置上限", viewModel.CaptionText);
        Assert.Equal("未设置上限", viewModel.CurrentValue);
        Assert.Equal("Safe", viewModel.PaletteKey);
        Assert.True(viewModel.IsDataAvailable);
    }

    [Fact]
    public void Apply_WithUnsupportedSource_ShowsNeutralNoticeInsteadOfError()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            null,
            QuotaRisk.Safe,
            null,
            null,
            Now,
            null)
        {
            SourceName = "Codex++ 聚合模式",
            UnsupportedReason = "多个供应商轮转，无法确定单一余额。"
        };

        viewModel.Apply(state);

        Assert.Equal("暂不支持", viewModel.StatusText);
        Assert.True(viewModel.HasNotice);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.ShowsPercent);
        Assert.Equal("多个供应商轮转，无法确定单一余额。", viewModel.NoticeMessage);
    }

    [Fact]
    public void Apply_RaisesOneBatchedPropertyChange()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changes.Add(eventArgs.PropertyName);
        var state = new QuotaViewState(
            40,
            QuotaRisk.Warning,
            new QuotaWindow(60, TimeSpan.FromHours(5), Now.AddHours(4)),
            null,
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal(new string?[] { null }, changes);
    }

    [Fact]
    public void Apply_WithAccountTokenUsage_UsesWanAndYiOnly()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            7,
            QuotaRisk.Critical,
            null,
            new QuotaWindow(93, TimeSpan.FromDays(7), Now.AddDays(2)),
            Now,
            null)
        {
            TokenUsage = new TokenUsageSummary(248300, 260224711, 758444908)
        };

        viewModel.Apply(state);

        Assert.True(viewModel.HasTokenUsage);
        Assert.Equal("24.8万", viewModel.TokenToday);
        Assert.Equal("2.6亿", viewModel.TokenMonth);
        Assert.Equal("7.58亿", viewModel.TokenTotal);
    }

    [Fact]
    public void Apply_MapsReturnedDurationsToOfficialLimitTiles()
    {
        var viewModel = new OrbViewModel(new FixedTimeProvider(Now));
        var state = new QuotaViewState(
            82,
            QuotaRisk.Safe,
            new QuotaWindow(18, TimeSpan.FromDays(7), Now.AddDays(7)),
            new QuotaWindow(27, TimeSpan.FromHours(5), Now.AddHours(4)),
            Now,
            null);

        viewModel.Apply(state);

        Assert.Equal("5h 限额", viewModel.CurrentLabel);
        Assert.Equal("73% 剩余", viewModel.CurrentValue);
        Assert.Equal("一周限额", viewModel.WeeklyLabel);
        Assert.Equal("82% 剩余", viewModel.WeeklyValue);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
