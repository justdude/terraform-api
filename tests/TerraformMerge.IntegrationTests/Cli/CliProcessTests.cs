using TerraformMerge.IntegrationTests.Common;

namespace TerraformMerge.IntegrationTests.Cli;

/// <summary>
/// End-to-end tests of the real TerraformMerge.exe CLI: arg parsing, exit codes,
/// and on-disk output. Reliable signals are the exit code and the output files
/// (stdout from a WinExe under redirection is asserted only loosely).
/// </summary>
[Collection("cli")]
public class CliProcessTests
{
    // ---- convert (F1–F3) ----

    [Fact] // F1
    public void Convert_OpenApiFile_WritesTerraform_Exit0()
    {
        using var ws = new TempWorkspace();
        var input = ws.CopyFixture("convert-input.json");
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("convert", "--openapi", input, "--out", outPath,
            "--env", "dev", "--api-group", "catalog-api-group");

        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(outPath), "output file written");
        var tf = File.ReadAllText(outPath);
        Assert.Contains("api_operations", tf);
        Assert.Contains("products", tf);
    }

    [Fact] // F2
    public void Convert_OmittedSettings_EmitPlaceholderTags()
    {
        using var ws = new TempWorkspace();
        var input = ws.CopyFixture("convert-input.json");
        var outPath = ws.Path("out.tf");

        // Omit --env etc. — they must become {placeholder} tags, not fail.
        var r = TestEnvironment.RunCli("convert", "--openapi", input, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        Assert.Contains("{environment}", tf);
    }

    [Fact] // F3
    public void Convert_InvalidOpenApi_Exit1()
    {
        using var ws = new TempWorkspace();
        var input = ws.Write("broken.json", "{ \"openapi\": \"3.0.3\", \"paths\": ");
        var outPath = ws.Path("out.tf");

        var r = TestEnvironment.RunCli("convert", "--openapi", input, "--out", outPath);

        Assert.Equal(1, r.ExitCode);
        Assert.False(File.Exists(outPath), "no output written on failure");
    }

    // ---- merge (F4–F8, F12) ----

    [Fact] // F4
    public void Merge_TerraformTarget_AppendsOnlyMissing_Exit0()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("merged.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        // list-orders is equivalent (GET orders) so not duplicated; create is appended.
        Assert.Contains("create-order-staging", tf);
        Assert.Equal(3, OperationCount(tf));
    }

    [Fact] // F5
    public void Merge_OpenApiTarget_AutoDetected_AppendsNewOperation()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target-openapi.json");
        var outPath = ws.Path("merged.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        Assert.Contains("DELETE", tf);
        Assert.Contains("orders/{orderId}", tf);
    }

    [Fact] // F6
    public void Merge_OutDifferentPath_LeavesOriginalUntouched()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("merged.tf");
        var originalBefore = File.ReadAllText(original);

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(originalBefore, File.ReadAllText(original)); // untouched
        Assert.Equal(3, OperationCount(File.ReadAllText(outPath)));
    }

    [Fact] // F7
    public void Merge_NoOutArg_RewritesOriginalInPlace()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(3, OperationCount(File.ReadAllText(original)));
    }

    [Fact] // F8
    public void Merge_ThresholdOption_Accepted()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("original.tf");
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("merged.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target,
            "--out", outPath, "--threshold", "0.5");

        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(outPath));
    }

    [Fact] // F12
    public void Merge_CrlfOriginal_OutputStaysCrlf()
    {
        using var ws = new TempWorkspace();
        var crlf = TestEnvironment.FixtureText("original.tf").Replace("\r\n", "\n").Replace("\n", "\r\n");
        var original = ws.Write("original-crlf.tf", crlf);
        var target = ws.CopyFixture("target.tf");
        var outPath = ws.Path("merged.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target, "--out", outPath);

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        Assert.DoesNotContain('\n', tf.Replace("\r\n", "")); // no bare LF
    }

    [Fact] // F36
    public void Merge_EnvFlag_AppendsTheDevOperationsAsQa()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("qa.tf");        // one operation, qa
        var target = ws.CopyFixture("original.tf");    // two operations, dev
        var outPath = ws.Path("merged.tf");

        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target,
            "--out", outPath, "--env", "qa");

        Assert.Equal(0, r.ExitCode);
        var tf = File.ReadAllText(outPath);
        Assert.Contains("get-order-qa", tf);       // the missing operation, re-stamped
        Assert.DoesNotContain("-dev", tf);         // nothing dev-shaped reached the qa file
        Assert.Equal(2, OperationCount(tf));
    }

    [Fact] // F37
    public void Merge_EnvFlag_WithoutAValue_Exit1_AndWritesNothing()
    {
        using var ws = new TempWorkspace();
        var original = ws.CopyFixture("qa.tf");
        var target = ws.CopyFixture("original.tf");
        var outPath = ws.Path("merged.tf");
        var before = File.ReadAllText(original);

        // "--env" with nothing after it parses as the value "true".
        var r = TestEnvironment.RunCli("merge", "--original", original, "--target", target,
            "--out", outPath, "--env");

        Assert.Equal(1, r.ExitCode);
        Assert.False(File.Exists(outPath), "no output on a rejected value");
        Assert.Equal(before, File.ReadAllText(original));
    }

    // ---- diagnostics (F9–F11) ----

    [Theory] // F9
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help_Exit0(string verb)
    {
        var r = TestEnvironment.RunCli(verb);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact] // F10
    public void UnknownVerb_Exit1()
    {
        var r = TestEnvironment.RunCli("frobnicate");
        Assert.Equal(1, r.ExitCode);
    }

    [Fact] // F11
    public void Convert_MissingRequiredArg_Exit1()
    {
        using var ws = new TempWorkspace();
        var r = TestEnvironment.RunCli("convert", "--out", ws.Path("out.tf"));
        Assert.Equal(1, r.ExitCode);
    }

    /// <summary>Counts api_operation blocks by their operation_id lines.</summary>
    private static int OperationCount(string tf) =>
        tf.Split('\n').Count(l => l.TrimStart().StartsWith("operation_id", StringComparison.Ordinal));
}
