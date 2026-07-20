namespace QuotaOrb.Core.Tests.Support;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory() =>
        Root = Path.Combine(Path.GetTempPath(), "QuotaOrb.Tests", Guid.NewGuid().ToString("N"));

    public string Root { get; }

    public string CreateDirectory(string relative)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relative)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Array.Empty<byte>());
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
