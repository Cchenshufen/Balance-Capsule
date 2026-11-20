namespace QuotaOrb.Core.Settings;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    string? MonitorDeviceName = null,
    bool StartWithWindows = true,
    bool HideInFullscreen = true,
    bool AnimationsEnabled = true,
    AgentSource SelectedAgent = AgentSource.Codex,
    QuotaWindowMode QuotaWindow = QuotaWindowMode.FiveHour,
    int SchemaVersion = 1,
    bool ShowBothSources = false,
    double? ClaudeLeft = null,
    double? ClaudeTop = null,
    bool DarkDetailGlass = false,
    int DetailTransparency = 55);

public enum QuotaWindowMode
{
    FiveHour,
    Weekly
}
