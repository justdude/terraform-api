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

    // ---- merge --env ----

    /// <summary>A qa config holding one operation; the dev sample has three.</summary>
    private (string original, string target, string output) DevIntoQa()
    {
        var original = Path_("orders-qa.tf");
        var target = Path_("orders-dev.tf");
        File.WriteAllText(original, Fixture("sample-apim-qa.tf"));
        File.WriteAllText(target, Fixture("sample-apim.tf"));
        return (original, target, Path_("merged.tf"));
    }

    [Fact]
    public void Merge_Env_ReStampsEveryAppendedOperation()
    {
        var (original, target, output) = DevIntoQa();

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", "qa"]);

        Assert.Equal(0, exit);
        var text = File.ReadAllText(output);

        // The two operations qa was missing arrive as qa operations, id included.
        Assert.Contains("\"get-order-qa\"", text);
        Assert.Contains("\"create-order-qa\"", text);
        Assert.DoesNotContain("-dev", text);

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(3, reparsed.Nodes.Count);
        Assert.All(reparsed.Nodes, node => Assert.Equal("qa", node.Environment));
        Assert.All(reparsed.Nodes, node => Assert.Equal("rg-apim-qa", node.ApimResourceGroupName));
        Assert.All(reparsed.Nodes, node => Assert.Equal("apim-company-qa", node.ApimName));
        Assert.All(reparsed.Nodes, node => Assert.Equal("orders-api-qa", node.ApiName));
    }

    [Fact]
    public void Merge_WithoutEnv_KeepsTheTargetsIdButTakesTheOriginalsApimNames()
    {
        var (original, target, output) = DevIntoQa();

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target, "--out", output]);

        Assert.Equal(0, exit);
        var appended = _loader.LoadTerraform(File.ReadAllText(output), OperationSource.OriginalTerraform)
            .Nodes.Single(node => node.UrlTemplate == "orders/{orderId}");

        // The unchanged default, and the reason --env exists: the generated block
        // blends into the file it is written to — its APIM identifiers come from
        // the qa original — but the id it carries is still the dev file's.
        Assert.Equal("get-order-dev", appended.OperationId);
        Assert.Equal("rg-apim-qa", appended.ApimResourceGroupName);
        Assert.Equal("apim-company-qa", appended.ApimName);
        Assert.Equal("orders-api-qa", appended.ApiName);
    }

    [Fact]
    public void Merge_EnvNeitherFileNames_RewritesTheAppendedOperationsOwnValues()
    {
        var (original, target, output) = DevIntoQa();

        // uat is in neither file, so there are no values to copy: every field is
        // derived by rewriting the dev operation's own.
        Assert.Equal(0, CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", "uat"]));

        var nodes = _loader.LoadTerraform(File.ReadAllText(output), OperationSource.OriginalTerraform).Nodes;
        var appended = nodes.Single(node => node.UrlTemplate == "orders/{orderId}");
        Assert.Equal("get-order-uat", appended.OperationId);
        Assert.Equal("rg-apim-uat", appended.ApimResourceGroupName);
        Assert.Equal("orders-api-uat", appended.ApiName);

        // The qa file's own operation is untouched by an environment it was not asked about.
        Assert.Equal("qa", nodes.Single(node => node.OperationId == "list-orders-qa").Environment);
    }

    [Fact]
    public void Merge_Env_LeavesTheOriginalsOwnOperationByteForByte()
    {
        var (original, target, output) = DevIntoQa();
        var before = File.ReadAllText(original);

        Assert.Equal(0, CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", "qa"]));

        // Everything through the qa file's own operation — its api block, that
        // operation's every line and their column alignment — is reproduced
        // verbatim; only the appended blocks follow.
        var text = File.ReadAllText(output);
        var marker = "description              = \"Returns all orders\"";
        var end = before.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(end > 0, "fixture shape changed");
        var head = before[..(end + marker.Length)];
        Assert.Contains("operation_id             = \"list-orders-qa\"", head, StringComparison.Ordinal);
        Assert.StartsWith(head, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_EnvWithNoValue_IsRejectedInsteadOfStampingTrue()
    {
        var (original, target, output) = DevIntoQa();
        var before = File.ReadAllText(original);

        // A bare "--env" parses as the value "true"; stamping every operation
        // with an environment called "true" would be silent corruption.
        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env"]);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(output), "nothing written when the value is rejected");
        Assert.Equal(before, File.ReadAllText(original));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qa prod")]
    [InlineData("-qa")]
    [InlineData("rg/qa")]
    public void Merge_EnvValueThatIsNotAnEnvironmentName_ReturnsError(string value)
    {
        var (original, target, output) = DevIntoQa();

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", value]);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Merge_EnvAsSingleToken_IsAccepted()
    {
        var (original, target, output) = DevIntoQa();

        // The --key=value form goes through the same parser.
        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env=qa"]);

        Assert.Equal(0, exit);
        Assert.Contains("\"get-order-qa\"", File.ReadAllText(output));
    }

    [Fact]
    public void Merge_Env_StampsOpenApiOperationsWithThatEnvironmentsValues()
    {
        var original = Path_("orders-qa.tf");
        var target = Path_("orders-openapi.json");
        var output = Path_("merged.tf");
        File.WriteAllText(original, Fixture("sample-apim-qa.tf"));
        File.WriteAllText(target, Fixture("sample-openapi.json"));

        // OpenAPI operations carry no environment of their own, so there is no
        // token to rewrite — they take qa's values from the loaded qa config.
        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", "qa"]);

        Assert.Equal(0, exit);
        var reparsed = _loader.LoadTerraform(File.ReadAllText(output), OperationSource.OriginalTerraform);
        Assert.True(reparsed.Nodes.Count > 1, "the OpenAPI target adds operations");
        Assert.All(reparsed.Nodes, node => Assert.Equal("rg-apim-qa", node.ApimResourceGroupName));
        Assert.All(reparsed.Nodes, node => Assert.Equal("orders-api-qa", node.ApiName));
    }

    [Fact]
    public void Merge_EnvMatchingTheTargetsOwnEnvironment_ChangesNothingAndSucceeds()
    {
        var original = Path_("orders-dev.tf");
        var target = Path_("orders-openapi.json");
        var output = Path_("merged.tf");
        File.WriteAllText(original, Fixture("sample-apim.tf"));
        File.WriteAllText(target, Fixture("sample-apim.tf")); // dev into dev: nothing to add

        var exit = CliRunner.Run(["merge", "--original", original, "--target", target,
            "--out", output, "--env", "dev"]);

        Assert.Equal(0, exit);
        Assert.Equal(File.ReadAllText(original), File.ReadAllText(output)); // byte-for-byte
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
