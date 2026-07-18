using System.Runtime.InteropServices;
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

        var loader = new OperationSourceLoader();
        var original = loader.LoadTerraform(File.ReadAllText(originalPath), OperationSource.OriginalTerraform);
        var target = loader.Load(File.ReadAllText(targetPath),
            OperationSource.TargetTerraform, OperationSource.TargetOpenApi, targetPath);

        var merge = new MergeEngine();
        var desired = merge.AppendMissing(original.Nodes, target.Nodes, threshold);
        var added = desired.Count - original.Nodes.Count;

        var rewritten = new OriginalWriter().Rewrite(original, desired);
        WriteOutput(outPath, rewritten);

        Console.WriteLine($"Merged {target.Nodes.Count} target operation(s): {added} added, " +
                          $"{target.Nodes.Count - added} already present -> {outPath}");
        return 0;
    }

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
                                     [--out <path>] [--threshold <0..1>]

            Any conversion setting you omit is generated as a {placeholder} tag.
            Run with no arguments to open the graphical merge window.
            """);
        return code;
    }
}
