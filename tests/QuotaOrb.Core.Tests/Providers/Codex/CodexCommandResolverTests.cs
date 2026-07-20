using QuotaOrb.Core.Providers.Codex;
using QuotaOrb.Core.Tests.Support;

namespace QuotaOrb.Core.Tests.Providers.Codex;

public sealed class CodexCommandResolverTests
{
    [Fact]
    public void Resolve_PrefersKnownNpmLayoutOverNativeWindowsAppsCandidate()
    {
        using var fs = new TemporaryDirectory();
        var npm = fs.CreateDirectory("node");
        fs.CreateFile("node/node.exe");
        fs.CreateFile("node/codex.cmd");
        fs.CreateFile("node/node_modules/@openai/codex/bin/codex.js");
        var native = fs.CreateDirectory("WindowsApps/OpenAI.Codex/app/resources");
        fs.CreateFile("WindowsApps/OpenAI.Codex/app/resources/codex.exe");

        var result = CodexCommandResolver.Resolve(
            string.Join(Path.PathSeparator, npm, native),
            ".EXE;.CMD");

        Assert.EndsWith("node.exe", result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Single(result.PrefixArguments);
        Assert.EndsWith("codex.js", result.PrefixArguments[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AcceptsNativeExecutableWhenNpmLayoutIsAbsent()
    {
        using var fs = new TemporaryDirectory();
        var native = fs.CreateDirectory("native");
        fs.CreateFile("native/codex.exe");

        var result = CodexCommandResolver.Resolve(native, ".EXE;.CMD");

        Assert.EndsWith("codex.exe", result.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.PrefixArguments);
    }

    [Fact]
    public void Resolve_FallsBackToOfficialDesktopCacheWhenPathHasNoCli()
    {
        using var fs = new TemporaryDirectory();
        var desktopExecutable = fs.CreateFile("OpenAI/Codex/bin/0123456789abcdef/codex.exe");

        var result = CodexCommandResolver.Resolve(
            string.Empty,
            ".EXE;.CMD",
            fs.Root);

        Assert.Equal(desktopExecutable, result.FileName);
        Assert.Empty(result.PrefixArguments);
    }

    [Fact]
    public void Resolve_FallsBackToOfficialDesktopPackageWhenCacheIsAbsent()
    {
        using var fs = new TemporaryDirectory();
        var desktopExecutable = fs.CreateFile(
            "WindowsApps/OpenAI.Codex_26.707.8479.0_x64__2p2nqsd0c76g0/app/resources/codex.exe");

        var result = CodexCommandResolver.Resolve(
            string.Empty,
            ".EXE;.CMD",
            fs.CreateDirectory("LocalAppData"),
            fs.Root);

        Assert.Equal(desktopExecutable, result.FileName);
        Assert.Empty(result.PrefixArguments);
    }

    [Fact]
    public void Resolve_RejectsUnknownWrapper()
    {
        using var fs = new TemporaryDirectory();
        var unknown = fs.CreateDirectory("unknown");
        fs.CreateFile("unknown/codex.cmd");

        var error = Assert.Throws<FileNotFoundException>(() =>
            CodexCommandResolver.Resolve(unknown, ".EXE;.CMD"));

        Assert.Equal("Official Codex desktop runtime or CLI was not found.", error.Message);
    }
}
