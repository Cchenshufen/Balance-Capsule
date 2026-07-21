using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Settings;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace QuotaOrb.Windows.ViewModels;

public sealed class OrbViewModel : INotifyPropertyChanged
{
    private const double ChamberDiameter = 68d;
    private static readonly WpfBrush SafeBrush = CreateBrush(0x9F, 0xCA, 0xF3);
    private static readonly WpfBrush WarningBrush = CreateBrush(0xF5, 0xD4, 0x9E);
    private static readonly WpfBrush CriticalBrush = CreateBrush(0xF4, 0xAD, 0x9F);
    private static readonly WpfBrush ErrorBrush = CreateBrush(0xF2, 0xA0, 0xA6);
    private static readonly WpfBrush LoadingStatusBrush = CreateBrush(0x8C, 0xC7, 0xF2);
    private static readonly WpfBrush SafeStatusBrush = CreateBrush(0xFF, 0x99, 0x52);
    private static readonly WpfBrush WarningStatusBrush = CreateBrush(0xF5, 0xC9, 0x8A);
    private static readonly WpfBrush CriticalStatusBrush = CreateBrush(0xF5, 0x91, 0x85);
    private static readonly WpfBrush ErrorStatusBrush = CreateBrush(0xF2, 0x7A, 0x87);
    private readonly Dispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset? _lastSuccessfulAt;
    private QuotaWindowMode _quotaWindowMode = QuotaWindowMode.FiveHour;

    public OrbViewModel(
        TimeProvider? timeProvider = null,
        Dispatcher? dispatcher = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        DisplayText = "…";
        CaptionText = "加载中";
        PaletteKey = "Safe";
        LiquidBrush = SafeBrush;
        StatusBrush = LoadingStatusBrush;
        CurrentValue = "暂不可用";
        WeeklyValue = "暂不可用";
        CurrentLabel = "当前窗口";
        WeeklyLabel = "次级窗口";
        ResetValue = "暂不可用";
        LastRefreshValue = "暂无成功记录";
        ErrorMessage = string.Empty;
        NoticeMessage = string.Empty;
        SourceName = "Codex 官方";
        AgentName = "Codex";
        CurrentMetricTag = "5H";
        WeeklyMetricTag = "7D";
        TokenToday = "0万";
        TokenMonth = "0万";
        TokenTotal = "0万";
        LiquidHeight = ChamberDiameter * 0.36;
        AnimationsEnabled = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DisplayText { get; private set; }

    public double CompactDisplayFontSize { get; private set; } = 26d;

    public double DetailDisplayFontSize { get; private set; } = 40d;

    public string CaptionText { get; private set; }

    public double LiquidHeight { get; private set; }

    public string PaletteKey { get; private set; }

    public WpfBrush LiquidBrush { get; private set; }

    public WpfBrush StatusBrush { get; private set; }

    public string CurrentValue { get; private set; }

    public string WeeklyValue { get; private set; }

    public string CurrentLabel { get; private set; }

    public string WeeklyLabel { get; private set; }

    public string ResetValue { get; private set; }

    public string LastRefreshValue { get; private set; }

    public string ErrorMessage { get; private set; }

    public string NoticeMessage { get; private set; }

    public string SourceName { get; private set; }

    public string AgentName { get; private set; }

    public string CurrentMetricTag { get; private set; }

    public string WeeklyMetricTag { get; private set; }

    public double CurrentMetricFontSize { get; private set; } = 22d;

    public double WeeklyMetricFontSize { get; private set; } = 22d;

    public string TokenToday { get; private set; }

    public string TokenMonth { get; private set; }

    public string TokenTotal { get; private set; }

    public bool HasTokenUsage { get; private set; }

    public double CurrentRemainingPercent { get; private set; }

    public double WeeklyRemainingPercent { get; private set; }

    public bool HasError { get; private set; }

    public bool IsStale { get; private set; }

    public bool HasNotice { get; private set; }

    public bool ShowsPercent { get; private set; }

    public bool HasFiveHourQuota { get; private set; }

    public bool HasWeeklyQuota { get; private set; }

    public QuotaWindowMode EffectiveQuotaWindow { get; private set; } =
        QuotaWindowMode.FiveHour;

    public bool AnimationsEnabled { get; private set; }

    public double RemainingPercent { get; private set; }

    public double RemainingRatio { get; private set; }

    public string StatusText { get; private set; } = "加载中";

    public bool IsDataAvailable { get; private set; }

    public bool CanAnimate =>
        AnimationsEnabled && System.Windows.SystemParameters.ClientAreaAnimation;

    public void Apply(QuotaViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => Apply(state));
            return;
        }

        IsStale = state.IsStale;
        HasNotice = !string.IsNullOrWhiteSpace(state.UnsupportedReason);
        HasError = state.Risk == QuotaRisk.Error && !IsStale && !HasNotice;
        SourceName = !string.IsNullOrWhiteSpace(state.SourceName)
            ? state.SourceName
            : state.Balance?.DisplayName ?? "Codex 官方";
        NoticeMessage = state.UnsupportedReason ?? string.Empty;
        ErrorMessage = state.Error?.Message ?? string.Empty;
        ApplyTokenUsage(state.TokenUsage);

        if (HasNotice)
        {
            ApplyUnsupported();
        }
        else if (state.Balance is not null && !HasError)
        {
            ApplyBalance(state.Balance);
        }
        else
        {
            ApplyQuota(state);
        }

        if (IsStale)
        {
            StatusText = "数据已过期";
        }

        if (!HasError && !HasNotice && !IsStale && state.Risk != QuotaRisk.Loading)
        {
            _lastSuccessfulAt = state.Balance?.FetchedAt ?? state.UpdatedAt;
        }

        LastRefreshValue = _lastSuccessfulAt is null
            ? "暂无成功记录"
            : _lastSuccessfulAt.Value.ToLocalTime().ToString("HH:mm");

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => ApplySettings(settings));
            return;
        }

        AnimationsEnabled = settings.AnimationsEnabled;
        _quotaWindowMode = settings.QuotaWindow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetSelectedAgent(AgentSource source)
    {
        AgentName = source switch
        {
            AgentSource.Codex => "Codex",
            AgentSource.ClaudeCode => "Claude Code",
            _ => "Claude"
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetSelectedQuotaWindow(QuotaWindowMode mode) => _quotaWindowMode = mode;

    private void ApplyQuota(QuotaViewState state)
    {
        CompactDisplayFontSize = 26d;
        DetailDisplayFontSize = 40d;
        CurrentMetricFontSize = 22d;
        WeeklyMetricFontSize = 22d;
        var fiveHour = FindWindow(state, TimeSpan.FromHours(5)) ??
            (state.Current?.Duration is null ? state.Current : null);
        var weekly = FindWindow(state, TimeSpan.FromDays(7)) ??
            (state.Weekly?.Duration is null ? state.Weekly : null);
        HasFiveHourQuota = fiveHour is not null;
        HasWeeklyQuota = weekly is not null;
        var showWeekly = _quotaWindowMode == QuotaWindowMode.Weekly
            ? weekly is not null
            : fiveHour is null && weekly is not null;
        EffectiveQuotaWindow = showWeekly
            ? QuotaWindowMode.Weekly
            : QuotaWindowMode.FiveHour;
        var displayWindow = showWeekly ? weekly : fiveHour ?? weekly;
        var displayPercent = displayWindow is null
            ? (int?)null
            : (int)Math.Round(displayWindow.RemainingPercent, MidpointRounding.AwayFromZero);
        var displayRisk = state.Risk is QuotaRisk.Error or QuotaRisk.Loading
            ? state.Risk
            : GetRisk(displayPercent);
        PaletteKey = GetPaletteKey(displayRisk);
        LiquidBrush = GetBrush(displayRisk);
        StatusBrush = GetStatusBrush(displayRisk);
        DisplayText = state.Risk switch
        {
            QuotaRisk.Error => "!",
            QuotaRisk.Loading => "…",
            _ => displayPercent?.ToString() ?? "—"
        };
        CaptionText = state.Risk switch
        {
            QuotaRisk.Error => "读取失败",
            QuotaRisk.Loading => "加载中",
            _ => showWeekly ? "一周剩余" : "5h 剩余"
        };
        ShowsPercent = state.Risk is not QuotaRisk.Error and not QuotaRisk.Loading
            && displayPercent is not null;
        IsDataAvailable = ShowsPercent;
        RemainingPercent = IsDataAvailable
            ? Math.Clamp(displayPercent!.Value, 0d, 100d)
            : 0d;
        RemainingRatio = RemainingPercent / 100d;
        StatusText = IsStale ? "数据已过期" : displayRisk switch
        {
            QuotaRisk.Safe => "状态良好",
            QuotaRisk.Warning => "需要留意",
            QuotaRisk.Critical => "即将耗尽",
            QuotaRisk.Error => "读取失败",
            _ => "加载中"
        };
        LiquidHeight = displayRisk switch
        {
            QuotaRisk.Error => ChamberDiameter,
            QuotaRisk.Loading => ChamberDiameter * 0.36,
            _ => ChamberDiameter * RemainingRatio
        };
        CurrentValue = FormatWindow(fiveHour);
        WeeklyValue = FormatWindow(weekly);
        CurrentRemainingPercent = fiveHour?.RemainingPercent ?? 0d;
        WeeklyRemainingPercent = weekly?.RemainingPercent ?? 0d;
        CurrentLabel = "5h 限额";
        WeeklyLabel = "一周限额";
        CurrentMetricTag = "5H";
        WeeklyMetricTag = "7D";
        ResetValue = FormatReset(FindNextResetWindow(state));
    }

    private void ApplyBalance(BalanceSnapshot balance)
    {
        CompactDisplayFontSize = 18d;
        DetailDisplayFontSize = 28d;
        CurrentMetricFontSize = 24d;
        WeeklyMetricFontSize = 19d;
        var isKeyLimit = balance.Kind == BalanceKind.ApiKeyLimitRemaining;
        var semantic = isKeyLimit ? "密钥剩余额度" : "账户余额";
        var hasAmount = balance.Amounts.Count > 0;

        DisplayText = hasAmount ? FormatAmount(balance.Amounts[0]) : "—";
        CaptionText = hasAmount
            ? semantic
            : isKeyLimit && !string.IsNullOrWhiteSpace(balance.Note)
                ? "未设置上限"
                : "余额暂不可用";
        StatusText = hasAmount
            ? isKeyLimit ? "额度可用" : "余额可用"
            : CaptionText;
        PaletteKey = "Safe";
        LiquidBrush = SafeBrush;
        StatusBrush = SafeStatusBrush;
        ShowsPercent = false;
        HasFiveHourQuota = false;
        HasWeeklyQuota = false;
        IsDataAvailable = true;
        RemainingPercent = 0d;
        RemainingRatio = 0d;
        LiquidHeight = ChamberDiameter * 0.5;
        CurrentLabel = "当前余额";
        CurrentValue = hasAmount
            ? string.Join(" · ", balance.Amounts.Select(FormatAmount))
            : CaptionText;
        WeeklyLabel = "当前供应商";
        WeeklyValue = balance.DisplayName;
        CurrentMetricTag = "额度";
        WeeklyMetricTag = "API";
        ResetValue = "无重置时间";
        CurrentRemainingPercent = 0d;
        WeeklyRemainingPercent = 0d;
    }

    private void ApplyUnsupported()
    {
        CompactDisplayFontSize = 26d;
        DetailDisplayFontSize = 40d;
        DisplayText = "—";
        CaptionText = "暂不支持";
        StatusText = "暂不支持";
        PaletteKey = "Safe";
        LiquidBrush = SafeBrush;
        StatusBrush = SafeStatusBrush;
        ShowsPercent = false;
        HasFiveHourQuota = false;
        HasWeeklyQuota = false;
        IsDataAvailable = false;
        RemainingPercent = 0d;
        RemainingRatio = 0d;
        LiquidHeight = ChamberDiameter * 0.5;
        CurrentLabel = "说明";
        CurrentValue = NoticeMessage;
        WeeklyLabel = "来源";
        WeeklyValue = SourceName;
        CurrentMetricTag = "INFO";
        WeeklyMetricTag = "SRC";
        ResetValue = "暂不可用";
        CurrentRemainingPercent = 0d;
        WeeklyRemainingPercent = 0d;
    }

    private void ApplyTokenUsage(TokenUsageSummary? usage)
    {
        HasTokenUsage = usage is not null;
        TokenToday = FormatTokenCount(usage?.TodayTokens ?? 0);
        TokenMonth = FormatTokenCount(usage?.MonthTokens ?? 0);
        TokenTotal = FormatTokenCount(usage?.TotalTokens ?? 0);
    }

    private static string FormatTokenCount(long value)
    {
        var amount = Math.Max(0, value);
        return amount >= 100_000_000
            ? $"{FormatChineseNumber(amount / 100_000_000d)}亿"
            : $"{FormatChineseNumber(amount / 10_000d)}万";
    }

    private static string FormatChineseNumber(double value)
    {
        var format = value >= 100d ? "0" : value >= 10d ? "0.0" : "0.##";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatAmount(BalanceAmount amount)
    {
        var value = amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        return amount.Currency.Trim().ToUpperInvariant() switch
        {
            "USD" => $"${value}",
            "CNY" or "RMB" => $"¥{value}",
            var currency => $"{value} {currency}"
        };
    }

    private string FormatReset(QuotaWindow? window)
    {
        if (window?.ResetsAt is null)
        {
            return "暂不可用";
        }

        var remaining = window.ResetsAt.Value - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (remaining >= TimeSpan.FromHours(36))
        {
            var days = Math.Max(1, (int)Math.Round(
                remaining.TotalDays,
                MidpointRounding.AwayFromZero));
            return $"{days}天后";
        }

        if (remaining >= TimeSpan.FromHours(1))
        {
            var hours = Math.Max(1, (int)Math.Round(
                remaining.TotalHours,
                MidpointRounding.AwayFromZero));
            return $"{hours}小时后";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"{minutes}分钟后";
    }

    private static string FormatWindow(QuotaWindow? window) =>
        window is null
            ? "暂不可用"
            : $"{Math.Round(window.RemainingPercent, MidpointRounding.AwayFromZero):0}% 剩余";

    private static QuotaWindow? FindWindow(QuotaViewState state, TimeSpan duration) =>
        new[] { state.Current, state.Weekly }
            .FirstOrDefault(window => window?.Duration is not null &&
                Math.Abs((window.Duration.Value - duration).TotalMinutes) < 1d);

    private static QuotaWindow? FindNextResetWindow(QuotaViewState state) =>
        new[] { state.Current, state.Weekly }
            .Where(window => window is not null)
            .Cast<QuotaWindow>()
            .Where(window => window.ResetsAt is not null)
            .OrderBy(window => window.ResetsAt)
            .FirstOrDefault();

    private static string GetPaletteKey(QuotaRisk risk) => risk switch
    {
        QuotaRisk.Warning => "Warning",
        QuotaRisk.Critical => "Critical",
        QuotaRisk.Error => "Error",
        _ => "Safe"
    };

    private static QuotaRisk GetRisk(int? remainingPercent) => remainingPercent switch
    {
        > 40 => QuotaRisk.Safe,
        >= 20 => QuotaRisk.Warning,
        _ => QuotaRisk.Critical
    };

    private static WpfBrush GetBrush(QuotaRisk risk) => risk switch
    {
        QuotaRisk.Warning => WarningBrush,
        QuotaRisk.Critical => CriticalBrush,
        QuotaRisk.Error => ErrorBrush,
        _ => SafeBrush
    };

    private static WpfBrush GetStatusBrush(QuotaRisk risk) => risk switch
    {
        QuotaRisk.Safe => SafeStatusBrush,
        QuotaRisk.Warning => WarningStatusBrush,
        QuotaRisk.Critical => CriticalStatusBrush,
        QuotaRisk.Error => ErrorStatusBrush,
        _ => LoadingStatusBrush
    };

    private static WpfBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new WpfSolidColorBrush(WpfColor.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
