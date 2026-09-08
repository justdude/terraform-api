using System.Diagnostics;

namespace TerraformMerge.IntegrationTests.Common;

/// <summary>
/// Locates repo paths and the built <c>TerraformMerge.exe</c>, and runs it as a
/// real process so the CLI is tested end-to-end (arg parsing, exit codes, file
/// output) rather than in-process.
/// </summary>
internal static class TestEnvironment
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Configuration { get; } =
        AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";

    public static string TerraformMergeExe =>
        Path.Combine(RepoRoot, "src", "TerraformMerge", "bin", Configuration, "net10.0-windows", "TerraformMerge.exe");

    public static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string FixtureText(string name) => File.ReadAllText(FixturePath(name));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "terraform-api.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (terraform-api.slnx).");
    }

    public sealed record CliResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>Runs the real exe with the given args; reliable signals are ExitCode + output files.</summary>
    public static CliResult RunCli(params string[] args)
    {
        var exe = TerraformMergeExe;
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Built exe not found: {exe}. Build TerraformMerge first.");

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = RepoRoot
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start TerraformMerge.exe");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("TerraformMerge.exe did not exit within 30s.");
        }

        return new CliResult(process.ExitCode, stdout, stderr);
    }
}
