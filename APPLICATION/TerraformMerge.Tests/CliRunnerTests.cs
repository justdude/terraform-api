using TerraformMerge.Cli;
using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// Tests the headless CLI verbs by invoking <see cref="CliRunner.Run"/> against
/// temporary files and asserting exit codes and produced output.
/// </summary>
public class CliRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tm-cli-" + Guid.NewGuid().ToString("N"));
    private readonly OperationSourceLoader _loader = new();

    public CliRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Path_(string name) => Path.Combine(_dir, name);
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Convert_FromFile_WritesTerraform()
    {
        var openApi = Path_("openapi.json");
        var output = Path_("apim.tf");
        File.WriteAllText(openApi, Fixture("sample-openapi.json"));

        var exit = CliRunner.Run(["convert", "--openapi", openApi, "--out", output, "--env", "dev", "--api-group", "orders-api-group"]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(output));
        var text = File.ReadAllText(output);
        Assert.Contains("orders-api-group = {", text);
        Assert.Contains("api_operations = [", text);
    }

    [Fact]
    public void Convert_NoSettings_ProducesPlaceholderTaggedOutput()
    {
        var openApi = Path_("openapi.json");
        var output = Path_("apim.tf");
        File.WriteAllText(openApi, Fixture("sample-openapi.json"));

        var exit = CliRunner.Run(["convert", "--openapi", openApi, "--out", output]);

        Assert.Equal(0, exit);
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", File.ReadAllText(output));
    }

    [Fact]
    public void Convert_MissingOpenApiArg_ReturnsError()
    {
        var exit = CliRunner.Run(["convert", "--out", Path_("x.tf")]);
        Assert.Equal(1, exit);
    }

    [Fact]
    public void Convert_MissingOutArg_ReturnsError()
    {
        var openApi = Path_("openapi.json");
        File.WriteAllText(openApi, Fixture("sample-openapi.json"));
        var exit = CliRunner.Run(["convert", "--openapi", openApi]);
        Assert.Equal(1, exit);
    }

    [Fact]
    public void Merge_TerraformOriginalWithOpenApiTarget_AppendsAndWrites()
    {
        var original = Path_("orders.tf");
        var target = Path_("orders-openapi.json");
        var output = Path_("merged.tf");
        File.WriteAllText(original, Fixture("sample-apim.tf"));
        File.WriteAllText(target, Fixture("sample-openapi.json"));

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target, "--out", output]);

        Assert.Equal(0, exit);
        var reparsed = _loader.LoadTerraform(File.ReadAllText(output), OperationSource.OriginalTerraform);
        Assert.Equal(5, reparsed.Nodes.Count);
    }

    [Fact]
    public void Merge_DefaultsOutToOriginal()
    {
        var original = Path_("orders.tf");
        var target = Path_("target.tf");
        File.WriteAllText(original, Fixture("sample-apim.tf"));
        File.WriteAllText(target, Fixture("sample-apim.tf")); // identical → nothing added

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target]);

        Assert.Equal(0, exit);
        // Original overwritten in place, still 3 operations (self-merge adds nothing).
        var reparsed = _loader.LoadTerraform(File.ReadAllText(original), OperationSource.OriginalTerraform);
        Assert.Equal(3, reparsed.Nodes.Count);
    }

    [Fact]
    public void Merge_MissingArgs_ReturnsError()
    {
        Assert.Equal(1, CliRunner.Run(["merge", "--original", Path_("a.tf")]));
    }

    [Fact]
    public void Help_ReturnsZero()
    {
        Assert.Equal(0, CliRunner.Run(["help"]));
    }

    [Fact]
    public void UnknownVerb_ReturnsError()
    {
        Assert.Equal(1, CliRunner.Run(["frobnicate"]));
    }
}
