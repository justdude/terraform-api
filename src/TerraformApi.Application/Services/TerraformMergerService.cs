using System.Text;
using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Merges a newly-generated Terraform configuration with an existing one,
/// preserving any operation blocks that appear in the existing config but are
/// absent from the new OpenAPI spec (e.g. custom or manually-added operations).
/// </summary>
public sealed partial class TerraformMergerService : ITerraformMerger
{
    private readonly ITerraformGenerator _generator;

    public TerraformMergerService(ITerraformGenerator generator)
    {
        _generator = generator;
    }

    /// <summary>
    /// Generates fresh Terraform from <paramref name="newConfiguration"/> and then
    /// re-inserts any operation blocks from <paramref name="existingTerraform"/> whose
    /// <c>operation_id</c> values are not present in the new spec.
    /// </summary>
    /// <param name="existingTerraform">The current HCL that may contain custom operations.</param>
    /// <param name="newConfiguration">The parsed APIM configuration derived from the updated OpenAPI spec.</param>
    /// <returns>Merged HCL string.</returns>
    public string Merge(string existingTerraform, ApimConfiguration newConfiguration)
    {
        var existingOperationIds = ExtractExistingOperationIds(existingTerraform);
        var newOperationIds = newConfiguration.ApiOperations
            .Select(op => op.OperationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find operations in existing config that are NOT in the new OpenAPI spec
        // These are custom/manually added operations that should be preserved
        var preservedOperations = ExtractPreservedOperations(existingTerraform, existingOperationIds, newOperationIds);

        // Generate the new config
        var newConfig = _generator.Generate(newConfiguration);

        if (preservedOperations.Count == 0)
            return newConfig;

        // Insert preserved operations into the new config before the closing of api_operations
        return InsertPreservedOperations(newConfig, preservedOperations);
    }

    /// <summary>
    /// Scans the HCL string for all <c>operation_id = "..."</c> assignments and
    /// returns the set of IDs found (case-insensitive).
    /// </summary>
    private static HashSet<string> ExtractExistingOperationIds(string terraform)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = OperationIdRegex().Matches(terraform);

        foreach (Match match in matches)
        {
            ids.Add(match.Groups[1].Value);
        }

        return ids;
    }

    /// <summary>
    /// Determines which operation IDs from the existing config are absent in the new
    /// spec, then extracts the raw HCL block text for each of those operations so
    /// they can be re-inserted into the merged output.
    /// </summary>
    private static List<string> ExtractPreservedOperations(
        string terraform,
        HashSet<string> existingIds,
        HashSet<string> newIds)
    {
        var preserved = new List<string>();

        // Find operation IDs that exist in old config but not in new spec
        var idsToPreserve = existingIds.Where(id => !newIds.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (idsToPreserve.Count == 0)
            return preserved;

        // Extract the full operation blocks for preserved IDs
        var operationBlocks = ExtractOperationBlocks(terraform);

        foreach (var block in operationBlocks)
        {
            var idMatch = OperationIdRegex().Match(block);
            if (idMatch.Success && idsToPreserve.Contains(idMatch.Groups[1].Value))
            {
                preserved.Add(block);
            }
        }

        return preserved;
    }

    /// <summary>
    /// Parses the <c>api_operations = [...]</c> section of the HCL line-by-line,
    /// using a brace-depth counter to delimit individual operation blocks.
    /// </summary>
    private static List<string> ExtractOperationBlocks(string terraform)
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

            if (!inBlock && trimmed == "{")
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
            {
                break;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Inserts the preserved operation blocks into the <c>api_operations</c> list
    /// of the newly-generated HCL, immediately before the list's closing bracket.
    /// </summary>
    private static string InsertPreservedOperations(string newConfig, List<string> preservedOperations)
    {
        var lines = newConfig.Split('\n').ToList();

        // Find the closing bracket of api_operations
        var insertIndex = -1;
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Trim() == "]" && i > 0)
            {
                // Check if this is the api_operations closing bracket
                // by looking backwards for api_operations.
                // NOTE: Do NOT break early on " = [" — nested blocks like
                // "response = [" would prematurely abort the search.
                for (var j = i - 1; j >= 0; j--)
                {
                    if (lines[j].Contains("api_operations"))
                    {
                        insertIndex = i;
                        break;
                    }
                }
            }

            if (insertIndex >= 0) break;
        }

        if (insertIndex < 0)
            return newConfig;

        var sb = new StringBuilder();
        for (var i = 0; i < insertIndex; i++)
        {
            sb.AppendLine(lines[i]);
        }

        // Add preserved operations
        foreach (var block in preservedOperations)
        {
            sb.AppendLine("    {");
            // Re-indent the block content
            var blockLines = block.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l != "{" && l != "},")
                .Where(l => !string.IsNullOrWhiteSpace(l));

            foreach (var line in blockLines)
            {
                sb.AppendLine($"        {line}");
            }
            sb.AppendLine("    },");
        }

        for (var i = insertIndex; i < lines.Count; i++)
        {
            if (i < lines.Count - 1)
                sb.AppendLine(lines[i]);
            else
                sb.Append(lines[i]);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"operation_id\s*=\s*""([^""]+)""")]
    private static partial Regex OperationIdRegex();
}
