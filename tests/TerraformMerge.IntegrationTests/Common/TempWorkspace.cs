namespace TerraformMerge.IntegrationTests.Common;

/// <summary>A throwaway directory for one test; deleted on dispose.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tfmerge-it", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Path(string name) => System.IO.Path.Combine(Root, name);

    /// <summary>Copies a fixture into the workspace and returns the copy's path.</summary>
    public string CopyFixture(string fixtureName, string? asName = null)
    {
        var dest = Path(asName ?? fixtureName);
        File.Copy(TestEnvironment.FixturePath(fixtureName), dest, overwrite: true);
        return dest;
    }

    public string Write(string name, string content)
    {
        var dest = Path(name);
        File.WriteAllText(dest, content);
        return dest;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
