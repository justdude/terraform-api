using System.Text;
using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Transforms Terraform APIM configurations between environments using text-based
/// field-aware substitution. Preserves formatting, wrapper structure, and custom fields.
///
/// Algorithm:
/// 1. Auto-detect source environment from Terraform field values (or use explicit override).
/// 2. Extract key field values (resource group, APIM name, gateway host) from source.
/// 3. Apply exact value replacement + environment name pattern substitution.
/// 4. If an existing target Terraform is provided, merge: match operations by
///    url_template + HTTP method, sync from source, preserve target-only operations.
/// </summary>
public sealed partial class EnvironmentTransformerService : IEnvironmentTransformer
{
    /// <summary>
    /// Known environment name tokens used for auto-detection, ordered by specificity.
    /// </summary>
    private static readonly string[] KnownEnvironments =
        ["development", "production", "pre-prod", "preprod", "staging", "sandbox", "stage", "test", "prod", "dev", "stg", "uat", "qa"];

    /// <inheritdoc />
    public EnvironmentTransformResult Transform(
        string sourceTerraform,
        EnvironmentTransformSettings settings,
        string? existingTargetTerraform = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceTerraform))
                return Fail("Source Terraform content is required.");

            // 1. Detect source environment
            var sourceEnv = settings.SourceEnvironment ?? DetectSourceEnvironment(sourceTerraform);
            if (sourceEnv == null)
                return Fail(
                    "Could not auto-detect source environment from the Terraform content. " +
                    "Please provide the source environment name explicitly via SourceEnvironment.");

            if (sourceEnv.Equals(settings.TargetEnvironment, StringComparison.OrdinalIgnoreCase))
                return Fail(
                    $"Source and target environments are the same: '{sourceEnv}'. " +
                    "Please specify a different target environment.");

            // 2. Extract source field values for exact replacement
            var sourceRg = ExtractFirstFieldValue(sourceTerraform, "apim_resource_group_name");
            var sourceApim = ExtractFirstFieldValue(sourceTerraform, "apim_name");
            var sourceServiceUrl = ExtractFirstFieldValue(sourceTerraform, "service_url");
            var sourceGatewayHost = sourceServiceUrl != null ? ExtractHostFromUrl(sourceServiceUrl) : null;

            var warnings = new List<string>();
            if (sourceRg == null) warnings.Add("Could not extract apim_resource_group_name from source.");
            if (sourceApim == null) warnings.Add("Could not extract apim_name from source.");

            // 3. Apply transformations
            var transformed = sourceTerraform;

            // Replace exact known values (specific field values first, env patterns second)
            if (sourceRg != null)
                transformed = transformed.Replace(sourceRg, settings.TargetStageGroupName);
            if (sourceApim != null)
                transformed = transformed.Replace(sourceApim, settings.TargetApimName);
            if (sourceGatewayHost != null)
                transformed = transformed.Replace(sourceGatewayHost, settings.TargetApiGatewayHost);

            // Replace environment name patterns in identifiers and paths
            transformed = ReplaceEnvironmentPatterns(transformed, sourceEnv, settings.TargetEnvironment);

            // Override subscription_required if specified
            if (settings.TargetSubscriptionRequired.HasValue)
                transformed = ReplaceFieldBoolValue(transformed, "subscription_required",
                    settings.TargetSubscriptionRequired.Value);

            // 4. If no existing target, return transformed source directly
            if (existingTargetTerraform == null)
            {
                var ops = ExtractOperationRoutes(sourceTerraform);
                return new EnvironmentTransformResult
                {
                    Success = true,
                    TransformedTerraform = transformed,
                    DetectedSourceEnvironment = sourceEnv,
                    Summary = new TransformSummary
                    {
                        TotalOperations = ops.Count,
                        AddedOperations = ops
                    },
                    Warnings = warnings
                };
            }

            // 5. Merge with existing target
            return MergeWithExistingTarget(transformed, existingTargetTerraform, sourceEnv, warnings);
        }
        catch (Exception ex)
        {
            return Fail($"Transform failed: {ex.Message}");
        }
    }

    // ── Environment Detection ────────────────────────────────────────────

    /// <summary>
    /// Auto-detects the source environment name by examining field values in the Terraform text.
    /// Checks apim_resource_group_name, apim_name, name, path, and operation_id for
    /// known environment tokens appearing as distinct segments (after -, ., or _).
    /// </summary>
    internal static string? DetectSourceEnvironment(string terraform)
    {
        var fieldsToCheck = new[] { "apim_resource_group_name", "apim_name", "name", "path", "operation_id" };

        foreach (var field in fieldsToCheck)
        {
            var value = ExtractFirstFieldValue(terraform, field);
            if (value == null) continue;

            foreach (var env in KnownEnvironments)
            {
                // Match env as a distinct segment: after a separator, before a separator or end
                if (Regex.IsMatch(value, $@"[\-._/]{Regex.Escape(env)}(?=[\-._/""'\s,\]}}]|$)",
                        RegexOptions.IgnoreCase))
                    return env.ToLowerInvariant();
            }
        }

        return null;
    }

    // ── Field Extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts the first quoted string value of a named HCL field.
    /// Matches patterns like: <c>field_name = "value"</c> (with arbitrary whitespace alignment).
    /// </summary>
    internal static string? ExtractFirstFieldValue(string terraform, string fieldName)
    {
        var match = Regex.Match(terraform, $@"{Regex.Escape(fieldName)}\s*=\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Extracts the hostname from a URL string.
    /// Example: "https://api-dev.company.com/my-service" → "api-dev.company.com".
    /// </summary>
    internal static string? ExtractHostFromUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;

        // Fallback regex for non-standard URLs
        var match = Regex.Match(url, @"https?://([^/\s""]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    // ── Environment Pattern Replacement ──────────────────────────────────

    /// <summary>
    /// Replaces environment name patterns in the Terraform text while avoiding false positives.
    /// Only matches the source environment token when it appears as a distinct segment
    /// (after <c>-</c>, <c>.</c>, or <c>_</c>) and before a word boundary character.
    /// </summary>
    internal static string ReplaceEnvironmentPatterns(string terraform, string sourceEnv, string targetEnv)
    {
        var escaped = Regex.Escape(sourceEnv);

        // After hyphen: "-dev" → "-staging" (name, operation_id, api_name, product_id)
        terraform = Regex.Replace(terraform,
            $@"(?<=\-){escaped}(?=[""'\s\-./,\]}}]|$)",
            targetEnv,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // After dot: ".dev/" or ".dev" at value boundary (path segments)
        terraform = Regex.Replace(terraform,
            $@"(?<=\.){escaped}(?=[/""'\s.\-,\]}}]|$)",
            targetEnv,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Display name suffix: " - dev" → " - staging"
        terraform = Regex.Replace(terraform,
            $@"(?<= - ){escaped}(?=[""'])",
            targetEnv,
            RegexOptions.IgnoreCase);

        // After underscore: "_dev" → "_staging" (underscore-separated identifiers)
        terraform = Regex.Replace(terraform,
            $@"(?<=_){escaped}(?=[""'\s\-._/,\]}}]|$)",
            targetEnv,
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        return terraform;
    }

    /// <summary>
    /// Replaces every occurrence of a boolean field's value in the Terraform text.
    /// </summary>
    internal static string ReplaceFieldBoolValue(string terraform, string fieldName, bool newValue)
    {
        return Regex.Replace(terraform,
            $@"({Regex.Escape(fieldName)}\s*=\s*)(true|false)",
            $"${{1}}{(newValue ? "true" : "false")}",
            RegexOptions.IgnoreCase);
    }

    // ── Operation Extraction ─────────────────────────────────────────────

    /// <summary>
    /// Extracts individual operation blocks from the <c>api_operations = [...]</c> section
    /// using a brace-depth counter to delimit blocks.
    /// </summary>
    internal static List<string> ExtractOperationBlocks(string terraform)
    {
        var blocks = new List<string>();
        var inOperationsSection = false;
        var braceDepth = 0;
        var currentBlock = new StringBuilder();
        var inBlock = false;

        foreach (var line in terraform.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Contains("api_operations") && trimmed.Contains('['))
            {
                inOperationsSection = true;
                continue;
            }

            if (!inOperationsSection) continue;

            if (!inBlock && trimmed.StartsWith('{'))
            {
                inBlock = true;
                braceDepth = 1;
                currentBlock.Clear();
                currentBlock.AppendLine(line);
                continue;
            }

            if (inBlock)
            {
                currentBlock.AppendLine(line);

                foreach (var ch in trimmed)
                {
                    if (ch == '{') braceDepth++;
                    else if (ch == '}') braceDepth--;
                }

                if (braceDepth == 0)
                {
                    blocks.Add(currentBlock.ToString().TrimEnd());
                    inBlock = false;
                }
            }

            // End of api_operations section
            if (!inBlock && trimmed == "]")
                break;
        }

        return blocks;
    }

    /// <summary>
    /// Extracts operations from Terraform as a dictionary keyed by "METHOD url_template".
    /// </summary>
    internal static Dictionary<string, string> ExtractOperationsByRoute(string terraform)
    {
        var operations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in ExtractOperationBlocks(terraform))
        {
            var method = ExtractFirstFieldValue(block, "method");
            var urlTemplate = ExtractFirstFieldValue(block, "url_template");

            if (method != null && urlTemplate != null)
            {
                var key = $"{method.ToUpperInvariant()} {urlTemplate}";
                operations[key] = block;
            }
        }

        return operations;
    }

    /// <summary>
    /// Returns the list of operation route keys ("METHOD url_template") from Terraform.
    /// </summary>
    internal static List<string> ExtractOperationRoutes(string terraform)
    {
        return [.. ExtractOperationsByRoute(terraform).Keys];
    }

    // ── Merge Logic ──────────────────────────────────────────────────────

    /// <summary>
    /// Merges the transformed source Terraform with the existing target,
    /// preserving target-only operations (those not present in the source).
    /// </summary>
    private static EnvironmentTransformResult MergeWithExistingTarget(
        string transformedSource,
        string existingTarget,
        string sourceEnv,
        List<string> warnings)
    {
        var sourceOps = ExtractOperationsByRoute(transformedSource);
        var targetOps = ExtractOperationsByRoute(existingTarget);

        var synced = new List<string>();
        var added = new List<string>();
        var preserved = new List<string>();

        // Classify source operations
        foreach (var route in sourceOps.Keys)
        {
            if (targetOps.ContainsKey(route))
                synced.Add(route);
            else
                added.Add(route);
        }

        // Find target-only operations and extract their blocks
        var preservedBlocks = new List<string>();
        foreach (var (route, block) in targetOps)
        {
            if (!sourceOps.ContainsKey(route))
            {
                preserved.Add(route);
                preservedBlocks.Add(block);
            }
        }

        // Build merged terraform: transformed source + preserved target operations
        var merged = transformedSource;
        if (preservedBlocks.Count > 0)
            merged = InsertPreservedOperations(merged, preservedBlocks);

        return new EnvironmentTransformResult
        {
            Success = true,
            TransformedTerraform = merged,
            DetectedSourceEnvironment = sourceEnv,
            Summary = new TransformSummary
            {
                TotalOperations = sourceOps.Count + preservedBlocks.Count,
                SyncedOperations = synced,
                AddedOperations = added,
                PreservedOperations = preserved
            },
            Warnings = warnings
        };
    }

    /// <summary>
    /// Inserts preserved operation blocks into the <c>api_operations</c> list,
    /// immediately before the closing bracket.
    /// </summary>
    private static string InsertPreservedOperations(string terraform, List<string> preservedBlocks)
    {
        var lines = terraform.Split('\n').ToList();

        // Find the closing bracket of api_operations (search backwards)
        var insertIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Trim() != "]" || i <= 0) continue;

            for (var j = i - 1; j >= 0; j--)
            {
                if (lines[j].Contains("api_operations"))
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex >= 0) break;
        }

        if (insertIndex < 0)
            return terraform;

        var sb = new StringBuilder();
        for (var i = 0; i < insertIndex; i++)
            sb.AppendLine(lines[i]);

        // Insert preserved operations (they carry their original indentation)
        foreach (var block in preservedBlocks)
            sb.AppendLine(block);

        for (var i = insertIndex; i < lines.Count; i++)
        {
            if (i < lines.Count - 1)
                sb.AppendLine(lines[i]);
            else
                sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static EnvironmentTransformResult Fail(string error) =>
        new() { Success = false, Errors = [error] };
}
