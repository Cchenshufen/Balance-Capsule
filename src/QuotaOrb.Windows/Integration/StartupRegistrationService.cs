using Microsoft.Win32;
using System.IO;

namespace QuotaOrb.Windows.Integration;

public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuotaOrb";
    private readonly string _command;

    public StartupRegistrationService(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _command = $"\"{Path.GetFullPath(executablePath)}\"";
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return string.Equals(
            key?.GetValue(ValueName) as string,
            _command,
            StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup registry key is unavailable.");

        if (enabled)
        {
            key.SetValue(ValueName, _command, RegistryValueKind.String);
            var saved = key.GetValue(ValueName) as string;
            if (!string.Equals(saved, _command, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Windows startup registration could not be verified.");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            if (key.GetValue(ValueName) is not null)
            {
                throw new InvalidOperationException("Windows startup registration could not be removed.");
            }
        }
    }
}
