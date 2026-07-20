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
    int SchemaVersion = 1);

public enum QuotaWindowMode
{
    FiveHour,
    Weekly
}
