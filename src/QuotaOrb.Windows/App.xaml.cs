using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using QuotaOrb.Core.Domain;
using QuotaOrb.Core.Providers;
using QuotaOrb.Core.Providers.Claude;
using QuotaOrb.Core.Providers.Codex;
using QuotaOrb.Core.Settings;
using QuotaOrb.Windows.Integration;
using QuotaOrb.Windows.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;

namespace QuotaOrb.Windows;

public partial class App : System.Windows.Application
{
    private readonly SingleInstanceService _singleInstance = new();
    private readonly JsonSettingsStore _settingsStore = new();
    private readonly SemaphoreSlim _saveLock = new(1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<AgentSource, CapsuleWindow> _windows = new();
    private readonly Dictionary<AgentSource, IQuotaProvider> _providers = new()
    {
        [AgentSource.Codex] = CodexQuotaProvider.CreateDefault(),
        [AgentSource.ClaudeCode] = new ClaudeStatusQuotaProvider()
    };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer _rotationTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private AppSettings _settings = new();
    private StartupRegistrationService? _startup;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _trayMenu;
    private Drawing.Icon? _icon;
    private FullscreenMonitor? _fullscreen;
    private bool _orbsVisible = true;
    private bool _refreshing;
    private bool _refreshQueued;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            MessageBox.Show("Balance Capsule 仅支持 Windows 11 x64。", "Balance Capsule");
            Shutdown(-1);
            return;
        }

        if (!_singleInstance.TryAcquire())
        {
            Shutdown();
            return;
        }

        try
        {
            _settings = await _settingsStore.LoadAsync(_shutdown.Token);
            if (_settings.SelectedAgent != AgentSource.Codex)
                _settings = _settings with { SelectedAgent = AgentSource.ClaudeCode };

            var work = SystemParameters.WorkArea;
            var x = _settings.Left ?? Math.Max(work.Left + 12, work.Right - 530);
            var y = _settings.Top ?? Math.Max(work.Top + 12, work.Top + work.Height * .375 - CapsuleGlass.CompactHeight);
            var claudeX = _settings.ClaudeLeft ?? (x + 168 <= work.Right ? x + 88 : x - 88);
            var claudeY = _settings.ClaudeTop ?? y;
            CreateCapsule(AgentSource.Codex, new LogicalPoint(x, y));
            CreateCapsule(AgentSource.ClaudeCode, new LogicalPoint(claudeX, claudeY));

            _startup = new StartupRegistrationService(Environment.ProcessPath
                ?? throw new InvalidOperationException("无法定位当前可执行文件。"));
            // A legacy registration can still point at the previous executable after an upgrade.
            if (_settings.StartWithWindows)
            {
                try { _startup.SetEnabled(true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
                {
                    Log(exception);
                }
            }

            CreateTray();
            if (_settings.HideInFullscreen)
            {
                _fullscreen = new FullscreenMonitor(Dispatcher);
                _fullscreen.FullscreenChanged += (_, _) => ApplyPresentation();
                _fullscreen.Start();
            }
            ApplyPresentation();
            _refreshTimer.Tick += (_, _) => _ = RefreshAsync();
            _rotationTimer.Tick += (_, _) => RotateQuota();
            _refreshTimer.Start();
            _rotationTimer.Start();
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log(exception);
            MessageBox.Show("启动失败。请查看 %LOCALAPPDATA%\\BalanceCapsule\\startup.log。", "Balance Capsule");
            Shutdown(-1);
        }
    }

    private void CreateCapsule(AgentSource source, LogicalPoint origin)
    {
        var window = new CapsuleWindow(source == AgentSource.ClaudeCode, origin, BuildOrbMenu);
        var otherSource = source == AgentSource.Codex ? AgentSource.ClaudeCode : AgentSource.Codex;
        window.FindVisibleNeighbor = () => _windows.TryGetValue(otherSource, out var other) && other.IsVisible
            ? other.OrbPosition : null;
        window.RefreshRequested += () => _ = RefreshAsync();
        window.PositionCommitted += point =>
        {
            _settings = source == AgentSource.Codex
                ? _settings with { Left = point.X, Top = point.Y }
                : _settings with { ClaudeLeft = point.X, ClaudeTop = point.Y };
            _ = SaveSettingsAsync();
        };
        _windows.Add(source, window);
    }

    private IEnumerable<AgentSource> ActiveSources() => _settings.ShowBothSources
        ? [AgentSource.Codex, AgentSource.ClaudeCode]
        : [_settings.SelectedAgent];

    private void ApplyPresentation()
    {
        foreach (var (source, window) in _windows)
        {
            var glass = window.Glass;
            glass.Badge = _settings.ShowBothSources;
            glass.Dark = _settings.DarkDetailGlass;
            glass.Transparency = Math.Clamp(_settings.DetailTransparency, 25, 70) / 100d;
            if (_orbsVisible && _fullscreen?.IsFullscreen != true && (_settings.ShowBothSources || source == _settings.SelectedAgent))
            {
                if (!window.IsVisible) window.Show();
                glass.InvalidateVisual();
            }
            else if (window.IsVisible)
            {
                window.Hide();
            }
        }
        UpdateTray();
    }

    private async Task RefreshAsync()
    {
        if (_shutdown.IsCancellationRequested) return;
        if (_refreshing)
        {
            _refreshQueued = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshQueued = false;
                await Task.WhenAll(ActiveSources().Select(ReadSourceAsync));
            } while (_refreshQueued && !_shutdown.IsCancellationRequested);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task ReadSourceAsync(AgentSource source)
    {
        var glass = _windows[source].Glass;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            limit.CancelAfter(TimeSpan.FromSeconds(25));
            var snapshot = await Task.Run(() => _providers[source].ReadAsync(limit.Token), limit.Token);
            if (_shutdown.IsCancellationRequested) return;
            var hadBoth = glass.Snapshot is { Current: not null, Weekly: not null };
            glass.Snapshot = snapshot;
            glass.Error = null;
            if (!hadBoth || snapshot.Current is null || snapshot.Weekly is null)
                glass.Weekly = snapshot.Current is null;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            glass.Error = exception switch
            {
                QuotaProviderException { Code: "cli-not-found" } => "未找到 Codex 桌面运行时或 CLI。",
                QuotaProviderException { Code: "status-unavailable" } => "请在菜单安装 Claude Code 桥接，并在 Claude Code 中发送一条消息。",
                OperationCanceledException => "额度刷新超时。",
                QuotaProviderException providerError => providerError.Message,
                _ => "额度读取失败。"
            };
            if (exception is not QuotaProviderException and not OperationCanceledException)
                Log(exception);
        }
        glass.InvalidateVisual();
        UpdateTray();
    }

    private void RotateQuota()
    {
        foreach (var source in ActiveSources())
        {
            var glass = _windows[source].Glass;
            if (glass.Snapshot is { Current: not null, Weekly: not null })
                glass.Weekly = !glass.Weekly;
        }
        UpdateTray();
    }

    private void CreateTray()
    {
        _icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (Drawing.Icon)Drawing.SystemIcons.Information.Clone();
        _trayMenu = new Forms.ContextMenuStrip();
        _trayMenu.Opening += (_, _) =>
        {
            if (_trayMenu is null) return;
            var obsolete = _trayMenu.Items.Cast<Forms.ToolStripItem>().ToArray();
            _trayMenu.Items.Clear();
            foreach (var item in obsolete) item.Dispose();
            foreach (var item in MenuEntries()) _trayMenu.Items.Add(BuildTrayItem(item));
        };
        _tray = new Forms.NotifyIcon
        {
            Icon = _icon,
            ContextMenuStrip = _trayMenu,
            Text = "Balance Capsule",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => _ = RefreshAsync();
    }

    private void UpdateTray()
    {
        if (_tray is null) return;
        var text = string.Join(" · ", ActiveSources().Select(source =>
        {
            var glass = _windows[source].Glass;
            return glass.SourceName + " " + glass.QuotaText;
        }));
        _tray.Text = text.Length <= 63 ? text : text[..63];
    }

    private sealed record Entry(string Label, Action? Run = null, bool? Checked = null, IReadOnlyList<Entry>? Children = null);

    private IReadOnlyList<Entry> MenuEntries() =>
    [
        new("立即刷新", () => _ = RefreshAsync()),
        new(_orbsVisible ? "隐藏悬浮球" : "显示悬浮球", () => { _orbsVisible = !_orbsVisible; ApplyPresentation(); }),
        new("显示来源", Children:
        [
            new("仅显示 Codex", () => SelectSource(AgentSource.Codex, false), !_settings.ShowBothSources && _settings.SelectedAgent == AgentSource.Codex),
            new("仅显示 Claude Code", () => SelectSource(AgentSource.ClaudeCode, false), !_settings.ShowBothSources && _settings.SelectedAgent == AgentSource.ClaudeCode),
            new("同时显示 Codex 和 Claude Code", () => SelectSource(AgentSource.Codex, true), _settings.ShowBothSources)
        ]),
        new("详情玻璃效果", Children:
        [
            new("标准液态毛玻璃", () => UpdateSettings(_settings with { DarkDetailGlass = false }), !_settings.DarkDetailGlass),
            new("深色液态玻璃", () => UpdateSettings(_settings with { DarkDetailGlass = true }), _settings.DarkDetailGlass)
        ]),
        new("详情透明度", Children: new[] { 70, 55, 40, 25 }
            .Select(percent => new Entry($"{percent}%", () => UpdateSettings(_settings with { DetailTransparency = percent }),
                _settings.DetailTransparency == percent)).ToArray()),
        new("登录时启动", SetStartup, _settings.StartWithWindows),
        new("安装 Claude Code 桥接…", InstallClaudeBridge),
        new("关于 Balance Capsule", () => MessageBox.Show("BalanceCapsule-win.15\\nWindows 11 x64", "Balance Capsule")),
        new("退出", () => _ = ExitAsync())
    ];

    private ContextMenu BuildOrbMenu()
    {
        var menu = new ContextMenu();
        foreach (var entry in MenuEntries()) menu.Items.Add(BuildOrbItem(entry));
        return menu;
    }

    private static MenuItem BuildOrbItem(Entry entry)
    {
        var item = new MenuItem { Header = entry.Label, IsCheckable = entry.Checked.HasValue, IsChecked = entry.Checked == true };
        if (entry.Run is not null) item.Click += (_, _) => entry.Run();
        if (entry.Children is not null)
            foreach (var child in entry.Children) item.Items.Add(BuildOrbItem(child));
        return item;
    }

    private static Forms.ToolStripMenuItem BuildTrayItem(Entry entry)
    {
        var item = new Forms.ToolStripMenuItem(entry.Label) { Checked = entry.Checked == true };
        if (entry.Run is not null) item.Click += (_, _) => entry.Run();
        if (entry.Children is not null)
            foreach (var child in entry.Children) item.DropDownItems.Add(BuildTrayItem(child));
        return item;
    }

    private void SelectSource(AgentSource source, bool both)
    {
        UpdateSettings(_settings with { SelectedAgent = source, ShowBothSources = both });
        _ = RefreshAsync();
    }

    private void SetStartup()
    {
        try
        {
            var enabled = !_settings.StartWithWindows;
            _startup?.SetEnabled(enabled);
            UpdateSettings(_settings with { StartWithWindows = enabled });
        }
        catch (Exception exception)
        {
            Log(exception);
            MessageBox.Show("未能更新登录时启动设置。", "Balance Capsule");
        }
    }

    private static void InstallClaudeBridge()
    {
        try
        {
            var result = ClaudeStatusLineInstaller.EnsureInstalled();
            MessageBox.Show(result == ClaudeStatusLineInstallResult.ExistingStatusLine
                ? "检测到已有 Claude Code statusLine，原设置已保留。请在现有 statusLine 中接入额度脚本。"
                : "桥接已就绪。请在 Claude Code 中发送一条消息，再刷新额度。", "Balance Capsule");
        }
        catch (Exception exception)
        {
            Log(exception);
            MessageBox.Show("无法安装 Claude Code 桥接。请检查 Claude Code 设置文件。", "Balance Capsule");
        }
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        ApplyPresentation();
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        await _saveLock.WaitAsync();
        try { await _settingsStore.SaveAsync(_settings); }
        catch (Exception exception) { Log(exception); }
        finally { _saveLock.Release(); }
    }

    private async Task ExitAsync()
    {
        await _saveLock.WaitAsync();
        _saveLock.Release();
        Shutdown();
    }

    private static void Log(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BalanceCapsule");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"[{DateTimeOffset.Now:O}] {exception}\\n");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _refreshTimer.Stop();
        _rotationTimer.Stop();
        _fullscreen?.Dispose();
        _tray?.Dispose();
        _trayMenu?.Dispose();
        _icon?.Dispose();
        _singleInstance.Dispose();
        base.OnExit(e);
    }
}
