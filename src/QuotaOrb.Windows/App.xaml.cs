using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers.Balance;
using QuotaOrb.Core.Providers.Claude;
using QuotaOrb.Core.Providers.Codex;
using QuotaOrb.Core.Providers.Detection;
using QuotaOrb.Core.Refresh;
using QuotaOrb.Core.Settings;
using QuotaOrb.Windows.Integration;
using QuotaOrb.Windows.ViewModels;
using QuotaOrb.Windows.Windows;

namespace QuotaOrb.Windows;

public partial class App : System.Windows.Application
{
    private readonly SingleInstanceService _singleInstance = new();
    private JsonSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private StartupRegistrationService? _startupRegistration;
    private QuotaStateService? _quotaState;
    private SafeBalanceHttpClient? _balanceHttpClient;
    private OrbViewModel? _viewModel;
    private FloatingOrbWindow? _orbWindow;
    private DetailFlyoutWindow? _detailWindow;
    private SettingsWindow? _settingsWindow;
    private FullscreenMonitor? _fullscreenMonitor;
    private TrayIconService? _trayIcon;
    private bool _isFullscreen;
    private bool _orbVisible = true;
    private int _cleanupStarted;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            System.Windows.MessageBox.Show(
                "BalanceCapsule-win.15 仅支持 Windows 11 x64。",
                "Balance Capsule",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown(-1);
            return;
        }

        if (!_singleInstance.TryAcquire())
        {
            Shutdown(0);
            return;
        }

        try
        {
            await InitializeApplicationAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            var logPath = WriteStartupError(exception);
            if (_viewModel is not null && _orbWindow is not null)
            {
                ShowUiError("startup", "Balance Capsule 启动失败，请检查本机环境后重试。");
                if (!_orbWindow.IsVisible)
                {
                    _orbWindow.Show();
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Balance Capsule 启动失败。\n\n诊断日志：{logPath}",
                    "Balance Capsule",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                await CleanupAsync().ConfigureAwait(true);
                Shutdown(-1);
            }
        }
    }

    private static string WriteStartupError(Exception exception)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BalanceCapsule");
        var logPath = Path.Combine(root, "startup.log");
        try
        {
            Directory.CreateDirectory(root);
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
            return logPath;
        }
        catch (Exception)
        {
            return "无法写入启动日志";
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (Volatile.Read(ref _cleanupStarted) == 0)
        {
            CleanupAsync().GetAwaiter().GetResult();
        }

        base.OnExit(e);
    }

    private async Task InitializeApplicationAsync()
    {
        _settingsStore = new JsonSettingsStore();
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(true);

        _viewModel = new OrbViewModel(dispatcher: Dispatcher);
        _viewModel.ApplySettings(_settings);
        _viewModel.SetSelectedAgent(_settings.SelectedAgent);
        _balanceHttpClient = new SafeBalanceHttpClient();
        _quotaState = CreateQuotaStateService(_settings.SelectedAgent);
        _quotaState.StateChanged += OnQuotaStateChanged;

        _detailWindow = new DetailFlyoutWindow(_viewModel);
        _orbWindow = new FloatingOrbWindow(_viewModel);
        _orbWindow.AttachDetailWindow(_detailWindow);
        _orbWindow.SetStartupEnabled(_settings.StartWithWindows);
        _orbWindow.SetSelectedAgent(_settings.SelectedAgent);
        _orbWindow.SetSelectedQuotaWindow(_settings.QuotaWindow);
        _orbWindow.SetQuotaWindowAvailability(
            _viewModel.ShowsPercent,
            _viewModel.HasFiveHourQuota,
            _viewModel.HasWeeklyQuota);
        _orbWindow.PositionCommitted += OnPositionCommitted;
        _orbWindow.RefreshRequested += OnRefreshRequested;
        _orbWindow.StartupToggleRequested += OnStartupToggleRequested;
        _orbWindow.AgentSourceRequested += OnAgentSourceRequested;
        _orbWindow.QuotaWindowRequested += OnQuotaWindowRequested;
        _orbWindow.SettingsRequested += OnSettingsRequested;
        _orbWindow.ExitRequested += OnExitRequested;

        ApplyInitialPosition(_orbWindow, _settings);
        MainWindow = _orbWindow;
        _viewModel.Apply(_quotaState.Current);
        _orbWindow.Show();
        ClampInitialPosition();

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The application executable path is unavailable.");
        _startupRegistration = new StartupRegistrationService(executablePath);

        Exception? startupError = null;
        try
        {
            _startupRegistration.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            startupError = exception;
        }

        _trayIcon = new TrayIconService(
            _settings.StartWithWindows,
            _settings.SelectedAgent,
            _settings.QuotaWindow);
        _trayIcon.RefreshRequested += OnRefreshRequested;
        _trayIcon.OrbVisibilityToggleRequested += OnOrbVisibilityToggleRequested;
        _trayIcon.StartupToggleRequested += OnStartupToggleRequested;
        _trayIcon.AgentSourceRequested += OnAgentSourceRequested;
        _trayIcon.QuotaWindowRequested += OnQuotaWindowRequested;
        _trayIcon.SettingsRequested += OnSettingsRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.SetQuotaWindowAvailability(
            _viewModel.ShowsPercent,
            _viewModel.HasFiveHourQuota,
            _viewModel.HasWeeklyQuota);
        _trayIcon.SetOrbVisible(_orbVisible);

        _fullscreenMonitor = new FullscreenMonitor(Dispatcher);
        _fullscreenMonitor.FullscreenChanged += OnFullscreenChanged;
        _fullscreenMonitor.Start();

        await _quotaState.StartAsync().ConfigureAwait(true);

        if (startupError is not null)
        {
            ShowUiError("startup", "开机启动设置未能写入当前用户配置。");
        }
    }

    private static void ApplyInitialPosition(FloatingOrbWindow window, AppSettings settings)
    {
        if (settings.Left is not null && settings.Top is not null)
        {
            window.Left = settings.Left.Value;
            window.Top = settings.Top.Value;
            return;
        }

        var work = System.Windows.SystemParameters.WorkArea;
        window.Left = work.Right - 84 - 12;
        window.Top = work.Top + Math.Max(12, (work.Height - 84) / 3d);
    }

    private void ClampInitialPosition()
    {
        if (_orbWindow is null)
        {
            return;
        }

        var clamped = WindowPlacementService.ClampAndSnapForWindow(
            _orbWindow,
            new LogicalPoint(_orbWindow.Left, _orbWindow.Top),
            new LogicalSize(84, 84),
            snapDistance: 12);
        _orbWindow.Left = clamped.X;
        _orbWindow.Top = clamped.Y;
    }

    private void OnQuotaStateChanged(object? sender, QuotaViewState state)
    {
        if (Dispatcher.HasShutdownStarted || Volatile.Read(ref _cleanupStarted) != 0)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            _viewModel?.Apply(state);
            if (_viewModel is not null)
            {
                _orbWindow?.SetSelectedQuotaWindow(_viewModel.EffectiveQuotaWindow);
                _orbWindow?.SetQuotaWindowAvailability(
                    _viewModel.ShowsPercent,
                    _viewModel.HasFiveHourQuota,
                    _viewModel.HasWeeklyQuota);
                _trayIcon?.SetSelectedQuotaWindow(_viewModel.EffectiveQuotaWindow);
                _trayIcon?.SetQuotaWindowAvailability(
                    _viewModel.ShowsPercent,
                    _viewModel.HasFiveHourQuota,
                    _viewModel.HasWeeklyQuota);
            }
            _trayIcon?.SetTooltip(FormatTooltip(state, _viewModel));
        }, DispatcherPriority.DataBind);
    }

    private static string FormatTooltip(QuotaViewState state, OrbViewModel? viewModel)
    {
        var source = string.IsNullOrWhiteSpace(state.SourceName)
            ? "Balance Capsule"
            : state.SourceName;

        if (!string.IsNullOrWhiteSpace(state.UnsupportedReason))
        {
            return $"{source}：余额查询暂不支持";
        }

        if (state.Balance is { } balance)
        {
            var value = balance.Amounts.FirstOrDefault();
            var balanceText = value is null
                ? balance.Kind == BalanceKind.ApiKeyLimitRemaining
                    ? "密钥未设置额度上限"
                    : "余额暂不可用"
                : $"{value.Amount:0.00} {value.Currency.ToUpperInvariant()}";
            return state.IsStale
                ? $"{source}：{balanceText}（数据已过期）"
                : $"{source}：{balanceText}";
        }

        var quotaText = viewModel?.ShowsPercent == true
            ? $"{viewModel.CaptionText} {viewModel.DisplayText}%"
            : $"剩余 {state.DisplayPercent}%";
        return state.IsStale
            ? $"{source} {quotaText}（数据已过期）"
            : state.Risk switch
            {
                QuotaRisk.Error => $"{source}：配额读取失败",
                QuotaRisk.Loading => $"{source}：配额加载中",
                _ => $"{source} {quotaText}"
            };
    }

    private QuotaStateService CreateQuotaStateService(AgentSource source)
    {
        var httpClient = _balanceHttpClient
            ?? throw new InvalidOperationException("Balance HTTP client is unavailable.");
        var ccSwitch = new CcSwitchSqliteDataSource();
        var balanceProviders = new Dictionary<string, IBalanceProvider>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek"] = new DeepSeekBalanceProvider(httpClient),
            ["openrouter"] = new OpenRouterBalanceProvider(httpClient)
        };

        if (source is AgentSource.Claude or AgentSource.ClaudeCode)
        {
            var isDesktop = source == AgentSource.Claude;
            Func<IReadOnlyCollection<int>> readAgentProcessIds =
                () => ReadAgentProcessIds("claude");
            if (!isDesktop)
            {
                TryEnsureClaudeStatusLine();
            }

            return new QuotaStateService(
                new ClaudeStatusQuotaProvider(),
                new StableActiveProviderDetector(
                    new ClaudeActiveProviderDetector(
                        ccSwitch: ccSwitch,
                        ccSwitchAppType: isDesktop ? "claude-desktop" : "claude",
                        hasRunningAgent: () => readAgentProcessIds().Count > 0),
                    readAgentProcessIds),
                balanceProviders,
                officialSourceName: isDesktop ? "Claude Desktop" : "Claude Code 官方");
        }

        return new QuotaStateService(
            CodexQuotaProvider.CreateDefault(),
            new StableActiveProviderDetector(
                new ActiveProviderDetector(ccSwitch: ccSwitch),
                () => ReadAgentProcessIds("codex")),
            balanceProviders);
    }

    private static IReadOnlyCollection<int> ReadAgentProcessIds(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void TryEnsureClaudeStatusLine()
    {
        try
        {
            ClaudeStatusLineInstaller.EnsureInstalled();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Claude remains selectable; the provider explains that no status data is available.
        }
    }

    private async void OnPositionCommitted(object? sender, LogicalPoint position)
    {
        try
        {
            _settings = _settings with
            {
                Left = position.X,
                Top = position.Y,
                MonitorDeviceName = GetCurrentMonitorDeviceName(position)
            };
            if (_settingsStore is not null)
            {
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ShowUiError("settings", "悬浮球位置未能保存。");
        }
    }

    private string? GetCurrentMonitorDeviceName(LogicalPoint position)
    {
        if (_orbWindow is null)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(_orbWindow);
        var devicePoint = new System.Drawing.Point(
            checked((int)Math.Round(position.X * dpi.DpiScaleX)),
            checked((int)Math.Round(position.Y * dpi.DpiScaleY)));
        return System.Windows.Forms.Screen.FromPoint(devicePoint).DeviceName;
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_quotaState is not null)
            {
                await _quotaState.RefreshNowAsync().ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ShowUiError("refresh", "手动刷新未能完成，请稍后重试。");
        }
    }

    private void OnOrbVisibilityToggleRequested(object? sender, EventArgs e)
    {
        _orbVisible = !_orbVisible;
        _detailWindow?.Hide();
        ApplyFullscreenVisibility();
        _trayIcon?.SetOrbVisible(_orbVisible);
    }

    private async void OnStartupToggleRequested(object? sender, bool enabled)
    {
        try
        {
            await ApplySettingsAsync(_settings with { StartWithWindows = enabled })
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            _trayIcon?.SetStartupEnabled(_settings.StartWithWindows);
            _orbWindow?.SetStartupEnabled(_settings.StartWithWindows);
            ShowUiError("startup", "开机启动设置未能更新。");
        }
    }

    private async void OnAgentSourceRequested(object? sender, AgentSource source)
    {
        if (source == _settings.SelectedAgent)
        {
            return;
        }

        try
        {
            await SwitchAgentAsync(source).ConfigureAwait(true);
        }
        catch (Exception)
        {
            _trayIcon?.SetSelectedAgent(_settings.SelectedAgent);
            _orbWindow?.SetSelectedAgent(_settings.SelectedAgent);
            ShowUiError("agent-switch", "数据来源切换失败，请稍后重试。");
            return;
        }

        _settings = _settings with { SelectedAgent = source };
        _viewModel?.SetSelectedAgent(source);
        _trayIcon?.SetSelectedAgent(source);
        _orbWindow?.SetSelectedAgent(source);
        try
        {
            if (_settingsStore is not null)
            {
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ShowUiError("settings", "数据来源已切换，但未能保存选择。");
        }
    }

    private async void OnQuotaWindowRequested(object? sender, QuotaWindowMode mode)
    {
        if (_viewModel is null ||
            !_viewModel.ShowsPercent ||
            mode == QuotaWindowMode.FiveHour && !_viewModel.HasFiveHourQuota ||
            mode == QuotaWindowMode.Weekly && !_viewModel.HasWeeklyQuota)
        {
            return;
        }

        if (mode == _settings.QuotaWindow)
        {
            return;
        }

        _settings = _settings with { QuotaWindow = mode };
        _viewModel?.SetSelectedQuotaWindow(mode);
        if (_quotaState is not null)
        {
            _viewModel?.Apply(_quotaState.Current);
            _trayIcon?.SetTooltip(FormatTooltip(_quotaState.Current, _viewModel));
        }
        _trayIcon?.SetSelectedQuotaWindow(mode);
        _orbWindow?.SetSelectedQuotaWindow(mode);

        try
        {
            if (_settingsStore is not null)
            {
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ShowUiError("settings", "额度窗口已切换，但未能保存选择。");
        }
    }

    private async Task SwitchAgentAsync(AgentSource source)
    {
        var next = CreateQuotaStateService(source);
        try
        {
            await next.StartAsync().ConfigureAwait(true);
        }
        catch
        {
            await next.DisposeAsync().ConfigureAwait(true);
            throw;
        }

        var previous = _quotaState;
        if (previous is not null)
        {
            previous.StateChanged -= OnQuotaStateChanged;
            await previous.DisposeAsync().ConfigureAwait(true);
        }

        _quotaState = next;
        next.StateChanged += OnQuotaStateChanged;
        _viewModel?.Apply(next.Current);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(_settings)
            {
                Owner = _orbWindow
            };
            _settingsWindow.SettingsConfirmed += OnSettingsConfirmed;
            _settingsWindow.Closed += OnSettingsWindowClosed;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception)
        {
            ShowUiError("settings", "设置窗口未能打开。");
        }
    }

    private async void OnSettingsConfirmed(object? sender, AppSettings settings)
    {
        try
        {
            await ApplySettingsAsync(settings with { SelectedAgent = _settings.SelectedAgent })
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            ShowUiError("settings", "设置未能保存。");
        }
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.SettingsConfirmed -= OnSettingsConfirmed;
        _settingsWindow.Closed -= OnSettingsWindowClosed;
        _settingsWindow = null;
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        if (_startupRegistration is not null &&
            settings.StartWithWindows != _settings.StartWithWindows)
        {
            _startupRegistration.SetEnabled(settings.StartWithWindows);
        }

        _settings = settings;
        _viewModel?.ApplySettings(settings);
        _trayIcon?.SetStartupEnabled(settings.StartWithWindows);
        _orbWindow?.SetStartupEnabled(settings.StartWithWindows);

        if (_isFullscreen)
        {
            ApplyFullscreenVisibility();
        }

        if (_settingsStore is not null)
        {
            await _settingsStore.SaveAsync(settings).ConfigureAwait(true);
        }
    }

    private void OnFullscreenChanged(object? sender, bool isFullscreen)
    {
        _isFullscreen = isFullscreen;
        ApplyFullscreenVisibility();
    }

    private void ApplyFullscreenVisibility()
    {
        if (_orbWindow is null)
        {
            return;
        }

        if (!_orbVisible || _isFullscreen && _settings.HideInFullscreen)
        {
            _detailWindow?.Hide();
            _orbWindow.Hide();
        }
        else if (!_orbWindow.IsVisible)
        {
            _orbWindow.Show();
        }
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        await CleanupAsync().ConfigureAwait(true);
        Shutdown(0);
    }

    private void ShowUiError(string code, string message)
    {
        var current = _quotaState?.Current;
        var state = new QuotaViewState(
            null,
            QuotaRisk.Error,
            current?.Current,
            current?.Weekly,
            DateTimeOffset.Now,
            new QuotaReadError(code, message))
        {
            SourceName = current?.SourceName
        };
        _viewModel?.Apply(state);
        _trayIcon?.SetTooltip("Balance Capsule：操作失败");
    }

    private async Task CleanupAsync()
    {
        if (Interlocked.Exchange(ref _cleanupStarted, 1) != 0)
        {
            return;
        }

        _fullscreenMonitor?.Dispose();
        if (_quotaState is not null)
        {
            _quotaState.StateChanged -= OnQuotaStateChanged;
            try
            {
                await _quotaState.DisposeAsync().ConfigureAwait(true);
            }
            catch
            {
                // Shutdown continues even when a provider process exits unexpectedly.
            }
        }

        _balanceHttpClient?.Dispose();
        _balanceHttpClient = null;

        _trayIcon?.Dispose();

        if (_settingsStore is not null)
        {
            try
            {
                await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            }
            catch
            {
                // A settings write failure must not leave the process running during shutdown.
            }
        }

        _settingsWindow?.Close();
        _detailWindow?.Close();
        _orbWindow?.Close();
        _singleInstance.Dispose();
    }
}
