using TerraformMerge.IntegrationTests.Common;

namespace TerraformMerge.IntegrationTests.Cli;

/// <summary>
/// Adversarial / negative CLI scenarios — the cases most likely to surface a
/// real defect: malformed input, no-op merges, heredoc preservation, the 3.1
/// downgrade, idempotency, and that generated output is itself valid HCL.
/// </summary>
[Collection("cli")]
public class CliEdgeCaseTests
{
    private static int OperationCount(string tf) =>
        tf.Split('\n').Count(l => l.TrimStart().StartsWith("operation_id", StringComparison.Ordinal));

    [Fact] // E1 — malformed original must fail gracefully, not crash
    public void Merge_MalformedOriginal_Exit1_NotCrash()
    {
        using var ws = new TempWorkspace();
        var original = ws.Write("bad.tf", "apis = { this is not valid hcl ]]]");
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(1, r.ExitCode); // handled failure, not an unhandled-exception code
    }

    [Fact] // E2 — self-merge is a no-op and byte-for-byte identical
    public void Merge_TargetSubsetOfOriginal_ByteForByteIdentical()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var before = File.ReadAllText(original);
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", original, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(before, File.ReadAllText(outPath));
    }

    [Fact] // E3 — appending an op must not disturb the api-block policy heredoc
    public void Merge_PreservesPolicyHeredoc()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("with-policy.tf");
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        Assert.Contains("<method>GET</method>", tf);
        Assert.Contains("<policies>", tf);
        Assert.Contains("create-order-staging", tf); // the append still happened
    }

    [Fact] // E4 — convert output is itself valid, reloadable HCL
    public void Convert_Output_IsReparseableTerraform()
    {
        using var ws = new TempWorkspace();
        var input = ws.CopyFixture("convert-input.json");
        var outPath = ws.Path("out.tf");
        Assert.Equal(0, TestEnvironment.RunCli("convert", "--openapi", input, "--out", outPath).ExitCode);

        // Feed the generated config straight back through merge as the original.
        var r = TestEnvironment.RunCli("merge", "--original", outPath, "--target", outPath, "--out", ws.Path("m.tf"));
        Assert.Equal(0, r.ExitCode);
        Assert.Equal(2, OperationCount(File.ReadAllText(outPath))); // GET + POST products
    }

    [Fact] // E5 — merge is idempotent: a second identical merge adds nothing
    public void Merge_Idempotent_SecondPassAddsNothing()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target.tf");
        var pass1 = ws.Path("p1.tf");
        var pass2 = ws.Path("p2.tf");

        Assert.Equal(0, TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", pass1).ExitCode);
        Assert.Equal(0, TestEnvironment.RunCli("merge", "--original", pass1, "--target", target, "--out", pass2).ExitCode);

        Assert.Equal(OperationCount(File.ReadAllText(pass1)), OperationCount(File.ReadAllText(pass2)));
    }

    [Fact] // E6 — OpenAPI 3.1 is downgraded and converts (no hard failure)
    public void Convert_OpenApi31_Succeeds()
    {
        using var ws = new TempWorkspace();
        var spec = TestEnvironment.FixtureText("convert-input.json").Replace("\"3.0.3\"", "\"3.1.0\"");
        var input = ws.Write("spec31.json", spec);
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("convert", "--openapi", input, "--out", outPath, "--env", "dev", "--api-group", "g");

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("api_operations", File.ReadAllText(outPath));
    }
}
