using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TerraformApi.Application;
using TerraformApi.Domain.Models;
using TerraformMerge.Engine;

namespace TerraformMerge.Cli;

/// <summary>
/// Headless command-line entry point. Two verbs:
///  <c>convert</c> — OpenAPI (file or URL) → APIM Terraform;
///  <c>merge</c>   — append operations from a Target file into an Original
///                   Terraform config using the similarity aligner.
/// Being a WinExe, the process attaches to the parent console for output.
/// </summary>
public static class CliRunner
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int AttachParentProcess = -1;

    public static int Run(string[] args)
    {
        AttachConsole(AttachParentProcess);

        try
        {
            var verb = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1));

            return verb switch
            {
                "convert" => RunConvert(options),
                "merge" => RunMerge(options),
                "help" or "-h" or "--help" or "/?" => PrintUsage(0),
                _ => Fail($"Unknown verb '{args[0]}'.")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int RunConvert(IReadOnlyDictionary<string, string> o)
    {
        var source = Require(o, "openapi", "--openapi <file|url> is required.");
        var outPath = Require(o, "out", "--out <path> is required.");

        var json = LooksLikeUrl(source) ? FetchUrl(source) : File.ReadAllText(source);

        var settings = new ConversionSettings
        {
            Environment = o.GetValueOrDefault("env"),
            ApiGroupName = o.GetValueOrDefault("api-group"),
            StageGroupName = o.GetValueOrDefault("stage-group"),
            ApimName = o.GetValueOrDefault("apim-name"),
            ApiPathPrefix = o.GetValueOrDefault("path-prefix"),
            ApiPathSuffix = o.GetValueOrDefault("path-suffix"),
            ApiGatewayHost = o.GetValueOrDefault("gateway-host"),
            BackendServicePath = o.GetValueOrDefault("backend-path")
        };

        var facade = TerraformApiFacade.Create();
        var result = facade.ConvertOpenApiToTerraform(json, settings);

        if (!result.Success)
            return Fail("Conversion failed: " + string.Join("; ", result.Errors));

        WriteOutput(outPath, result.TerraformConfig);
        Console.WriteLine($"Converted -> {outPath}");
        foreach (var warning in result.Warnings)
            Console.WriteLine("  warning: " + warning);
        return 0;
    }

    private static int RunMerge(IReadOnlyDictionary<string, string> o)
    {
        var originalPath = Require(o, "original", "--original <path> is required.");
        var targetPath = Require(o, "target", "--target <path> is required.");
        var outPath = o.GetValueOrDefault("out", originalPath);
        var threshold = o.TryGetValue("threshold", out var t) && double.TryParse(t, out var parsed)
            ? parsed
            : BlockAligner.DefaultThreshold;
        var environment = ReadEnvironment(o);

        var loader = new OperationSourceLoader();
        var original = loader.LoadTerraform(File.ReadAllText(originalPath), OperationSource.OriginalTerraform);
        var target = loader.Load(File.ReadAllText(targetPath),
            OperationSource.TargetTerraform, OperationSource.TargetOpenApi, targetPath);

        var merge = new MergeEngine();
        var appended = merge.AppendMissing(original.Nodes, target.Nodes, threshold);
        var added = appended.Count - original.Nodes.Count;

        var desired = appended;
        var restamped = 0;
        if (environment is not null)
            desired = Restamp(appended, environment, original, target, out restamped);

        var rewritten = new OriginalWriter().Rewrite(original, desired);
        WriteOutput(outPath, rewritten);

        Console.WriteLine($"Merged {target.Nodes.Count} target operation(s): {added} added, " +
                          $"{target.Nodes.Count - added} already present -> {outPath}");
        if (environment is not null)
            Console.WriteLine(EnvironmentReport(environment, added, restamped));
        return 0;
    }

    /// <summary>
    /// Moves the operations coming from the target into <paramref name="environment"/>
    /// — the headless form of the window's "add to Original as qa". Only the
    /// appended operations are touched: the original file's own blocks keep their
    /// AST and are re-emitted byte-for-byte. Each one is re-stamped on a copy, so
    /// the loaded target list stays as its file has it.
    /// </summary>
    private static List<OperationNode> Restamp(
        IReadOnlyList<OperationNode> appended,
        string environment,
        LoadedOperations original,
        LoadedOperations target,
        out int restamped)
    {
        var catalog = EnvironmentCatalog.Build(original.Nodes, target.Nodes);
        var result = new List<OperationNode>(appended.Count);
        restamped = 0;

        foreach (var node in appended)
        {
            if (node.Source == OperationSource.OriginalTerraform)
            {
                result.Add(node);
                continue;
            }

            var copy = node.Copy();
            if (EnvironmentRetargeter.Retarget(copy, environment, catalog))
                restamped++;
            result.Add(copy);
        }

        return result;
    }

    /// <summary>One line saying what <c>--env</c> did, including when it did nothing.</summary>
    private static string EnvironmentReport(string environment, int added, int restamped)
    {
        if (added == 0)
            return $"  environment: nothing was added, so nothing to re-stamp as {environment}.";
        if (restamped == added)
            return $"  environment: {restamped} added operation(s) re-stamped as {environment}.";
        if (restamped > 0)
            return $"  environment: {restamped} of {added} added operation(s) re-stamped as {environment}; " +
                   "the rest are already there or carry no environment name.";

        return $"  warning: --env {environment} re-stamped nothing — the added operations are " +
               "already in that environment, or name no environment this tool recognizes.";
    }

    /// <summary>
    /// The <c>--env</c> value for a merge, or null when the flag was not given.
    /// A bare <c>--env</c> with no value parses as "true" (see
    /// <see cref="ParseOptions"/>), which would otherwise silently stamp every
    /// added operation with an environment called "true" — so that, and anything
    /// that is not a plain environment name, is rejected.
    /// </summary>
    private static string? ReadEnvironment(IReadOnlyDictionary<string, string> o)
    {
        if (!o.TryGetValue("env", out var raw))
            return null;

        var value = (raw ?? "").Trim();
        if (value.Length == 0
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || !EnvironmentName.IsMatch(value))
        {
            throw new ArgumentException($"--env needs an environment name, e.g. --env qa (got '{raw}').");
        }

        return value;
    }

    private static readonly Regex EnvironmentName =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // -- helpers --

    private static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = arg[2..];
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                options[key[..eq]] = key[(eq + 1)..];
            }
            else if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = list[++i];
            }
            else
            {
                options[key] = "true";
            }
        }
        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string> o, string key, string message) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException(message);

    private static bool LooksLikeUrl(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");

    private static string FetchUrl(string url)
    {
        using var client = new HttpClient();
        return client.GetStringAsync(url).GetAwaiter().GetResult();
    }

    private static void WriteOutput(string path, string content)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("error: " + message);
        return 1;
    }

    private static int PrintUsage(int code)
    {
        Console.WriteLine("""
            Terraform Merge — command line

            Usage:
              TerraformMerge convert --openapi <file|url> --out <path>
                                     [--env <e>] [--api-group <g>] [--stage-group <rg>]
                                     [--apim-name <n>] [--path-prefix <p>] [--path-suffix <s>]
                                     [--gateway-host <h>] [--backend-path <b>]

              TerraformMerge merge   --original <path> --target <file|tf|openapi>
                                     [--out <path>] [--threshold <0..1>] [--env <e>]

            Any conversion setting you omit is generated as a {placeholder} tag.

            merge --env <e> re-stamps every operation it appends for environment
            <e> — apim_resource_group_name, apim_name, api_name, operation_id,
            display_name and description — taking the values that environment
            already uses in either file where they agree, else rewriting the
            operation's own (rg-apim-dev -> rg-apim-qa). Method, url_template and
            status_code are never touched, an operation's own ${...} values are
            left variable, and the original file's own operations are re-emitted
            byte-for-byte.

            Without --env an appended operation keeps the target's operation_id,
            display_name and description, while its apim_resource_group_name,
            apim_name and api_name come from the ORIGINAL file's api group — a
            generated block always blends into the file it is written to.

            Run with no arguments to open the graphical merge window.
            """);
        return code;
    }
}
