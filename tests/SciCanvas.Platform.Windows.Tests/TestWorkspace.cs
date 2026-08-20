namespace SciCanvas.Platform.Windows.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        string testRoot = Path.Combine(AppContext.BaseDirectory, "test-runs");
        Root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateFile(string name, ReadOnlySpan<byte> content)
    {
        string path = Path.Combine(Root, name);
        File.WriteAllBytes(path, content.ToArray());
        return path;
    }

    public void Dispose()
    {
        string basePath = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(Root);

        if (!fullRoot.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(fullRoot).Length != 32)
        {
            throw new InvalidOperationException("拒绝清理未验证的测试目录。");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }
    }
}

