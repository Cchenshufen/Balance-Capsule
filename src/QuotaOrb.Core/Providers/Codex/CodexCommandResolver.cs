namespace QuotaOrb.Core.Providers.Codex;

public static class CodexCommandResolver
{
    private const string NotFoundMessage = "Official Codex desktop runtime or CLI was not found.";

    public static CodexCommand Resolve(
        string path,
        string pathExt,
        string? localAppData = null,
        string? programFiles = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pathExt);

        var extensions = pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = EnumeratePathDirectories(path).ToArray();

        if (extensions.Contains(".EXE") && extensions.Contains(".CMD"))
        {
            foreach (var directory in directories)
            {
                var node = Path.Combine(directory, "node.exe");
                var wrapper = Path.Combine(directory, "codex.cmd");
                var script = Path.Combine(
                    directory,
                    "node_modules",
                    "@openai",
                    "codex",
                    "bin",
                    "codex.js");

                if (File.Exists(node) && File.Exists(wrapper) && File.Exists(script))
                {
                    return new CodexCommand(node, new[] { script });
                }
            }
        }

        if (extensions.Contains(".EXE"))
        {
            foreach (var directory in directories)
            {
                var executable = Path.Combine(directory, "codex.exe");
                if (File.Exists(executable))
                {
                    return new CodexCommand(executable, Array.Empty<string>());
                }
            }

            foreach (var executable in EnumerateOfficialDesktopExecutables(localAppData))
            {
                return new CodexCommand(executable, Array.Empty<string>());
            }

            foreach (var executable in EnumerateOfficialDesktopPackages(programFiles))
            {
                return new CodexCommand(executable, Array.Empty<string>());
            }
        }

        throw new FileNotFoundException(NotFoundMessage);
    }

    private static IEnumerable<string> EnumeratePathDirectories(string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length > 0 && seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    private static IReadOnlyList<string> EnumerateOfficialDesktopExecutables(string? localAppData)
    {
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Array.Empty<string>();
        }

        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (!Directory.Exists(binRoot))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .EnumerateDirectories(binRoot)
                .Where(directory => IsDesktopBuildDirectory(Path.GetFileName(directory)))
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsDesktopBuildDirectory(string name) =>
        name.Length == 16 && name.All(Uri.IsHexDigit);

    private static IReadOnlyList<string> EnumerateOfficialDesktopPackages(string? programFiles)
    {
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return Array.Empty<string>();
        }

        var windowsApps = Path.Combine(programFiles, "WindowsApps");
        if (!Directory.Exists(windowsApps))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory
                .EnumerateDirectories(windowsApps, "OpenAI.Codex_*")
                .Where(directory =>
                    Path.GetFileName(directory).EndsWith(
                        "__2p2nqsd0c76g0",
                        StringComparison.OrdinalIgnoreCase))
                .Select(directory => Path.Combine(directory, "app", "resources", "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
