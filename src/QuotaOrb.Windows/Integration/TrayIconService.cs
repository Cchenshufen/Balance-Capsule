using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using QuotaOrb.Core.Settings;

namespace QuotaOrb.Windows.Integration;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _startupItem;
    private readonly Forms.ToolStripMenuItem _codexItem;
    private readonly Forms.ToolStripMenuItem _claudeItem;
    private readonly Forms.ToolStripMenuItem _claudeCodeItem;
    private readonly Forms.ToolStripMenuItem _fiveHourItem;
    private readonly Forms.ToolStripMenuItem _weeklyItem;
    private readonly Forms.ToolStripMenuItem _quotaWindowItem;
    private readonly Forms.ToolStripMenuItem _orbVisibilityItem;
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _applicationIcon;
    private bool _disposed;

    public TrayIconService(
        bool startupEnabled,
        AgentSource selectedAgent,
        QuotaWindowMode quotaWindow)
    {
        var refreshItem = new Forms.ToolStripMenuItem("立即刷新");
        _orbVisibilityItem = new Forms.ToolStripMenuItem("隐藏悬浮球");
        _codexItem = new Forms.ToolStripMenuItem("Codex") { CheckOnClick = false };
        _claudeItem = new Forms.ToolStripMenuItem("Claude Desktop") { CheckOnClick = false };
        _claudeCodeItem = new Forms.ToolStripMenuItem("Claude Code") { CheckOnClick = false };
        var sourceItem = new Forms.ToolStripMenuItem("数据来源");
        sourceItem.DropDownItems.AddRange(new Forms.ToolStripItem[]
        {
            _codexItem,
            _claudeCodeItem
        });
        _fiveHourItem = new Forms.ToolStripMenuItem("5h 限额") { CheckOnClick = false };
        _weeklyItem = new Forms.ToolStripMenuItem("一周限额") { CheckOnClick = false };
        _quotaWindowItem = new Forms.ToolStripMenuItem("悬浮球显示");
        _quotaWindowItem.DropDownItems.AddRange(new Forms.ToolStripItem[]
        {
            _fiveHourItem,
            _weeklyItem
        });
        _startupItem = new Forms.ToolStripMenuItem("开机启动")
        {
            Checked = startupEnabled,
            CheckOnClick = false
        };
        var openSettingsItem = new Forms.ToolStripMenuItem("打开设置");
        var settingsItem = new Forms.ToolStripMenuItem("设置");
        settingsItem.DropDownItems.AddRange(new Forms.ToolStripItem[]
        {
            _startupItem,
            openSettingsItem
        });
        var exitItem = new Forms.ToolStripMenuItem("退出");

        refreshItem.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _orbVisibilityItem.Click += (_, _) =>
            OrbVisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
        _codexItem.Click += (_, _) => AgentSourceRequested?.Invoke(this, AgentSource.Codex);
        _claudeItem.Click += (_, _) => AgentSourceRequested?.Invoke(this, AgentSource.Claude);
        _claudeCodeItem.Click += (_, _) =>
            AgentSourceRequested?.Invoke(this, AgentSource.ClaudeCode);
        _fiveHourItem.Click += (_, _) =>
            QuotaWindowRequested?.Invoke(this, QuotaWindowMode.FiveHour);
        _weeklyItem.Click += (_, _) =>
            QuotaWindowRequested?.Invoke(this, QuotaWindowMode.Weekly);
        _startupItem.Click += (_, _) =>
            StartupToggleRequested?.Invoke(this, !_startupItem.Checked);
        openSettingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            refreshItem,
            _orbVisibilityItem,
            sourceItem,
            settingsItem,
            exitItem
        });

        _applicationIcon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (Drawing.Icon)Drawing.SystemIcons.Information.Clone();
        _icon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _applicationIcon,
            Text = "Balance Capsule 配额悬浮球",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        SetSelectedAgent(selectedAgent);
        SetSelectedQuotaWindow(quotaWindow);
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? OrbVisibilityToggleRequested;

    public event EventHandler<bool>? StartupToggleRequested;

    public event EventHandler<AgentSource>? AgentSourceRequested;

    public event EventHandler<QuotaWindowMode>? QuotaWindowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void SetStartupEnabled(bool enabled) => _startupItem.Checked = enabled;

    public void SetOrbVisible(bool visible) =>
        _orbVisibilityItem.Text = visible ? "隐藏悬浮球" : "显示悬浮球";

    public void SetSelectedAgent(AgentSource source)
    {
        _codexItem.Checked = source == AgentSource.Codex;
        _claudeItem.Checked = source == AgentSource.Claude;
        _claudeCodeItem.Checked = source == AgentSource.ClaudeCode;
    }

    public void SetSelectedQuotaWindow(QuotaWindowMode mode)
    {
        _fiveHourItem.Checked = mode == QuotaWindowMode.FiveHour;
        _weeklyItem.Checked = mode == QuotaWindowMode.Weekly;
    }

    public void SetQuotaWindowAvailability(
        bool quotaAvailable,
        bool fiveHourAvailable,
        bool weeklyAvailable)
    {
        _quotaWindowItem.Enabled = quotaAvailable;
        _fiveHourItem.Enabled = fiveHourAvailable;
        _weeklyItem.Enabled = weeklyAvailable;
    }

    public void SetTooltip(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon.Dispose();
        _menu.Dispose();
        _disposed = true;
    }
}
