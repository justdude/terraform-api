using System.Text;
using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Parses Terraform APIM HCL configuration and extracts a structured operations list.
/// Produces output in the same shape as the OpenAPI fetch tool for side-by-side comparison.
///
/// Extracts from:
/// - <c>api = [{ ... }]</c> block: API-level metadata (name, display_name, path, service_url)
/// - <c>api_operations = [{ ... }]</c> blocks: per-operation details
///   - Scalar fields: operation_id, method, url_template, display_name, description, status_code
///   - <c>request</c> sub-blocks: header, query_parameter, representation
///   - <c>response</c> sub-blocks: status_code per response entry
///   - Path parameters inferred from <c>{paramName}</c> placeholders in url_template
/// </summary>
public sealed class TerraformOperationsParserService : ITerraformOperationsParser
{
    /// <inheritdoc />
    public OperationsListResult Parse(string terraform)
    {
        if (string.IsNullOrWhiteSpace(terraform))
            return new OperationsListResult { Success = false, Error = "Terraform content is required." };

        try
        {
            var apiInfo = ExtractApiInfo(terraform);
            var operationBlocks = ExtractOperationBlocks(terraform);

            if (operationBlocks.Count == 0)
            {
                return new OperationsListResult
                {
                    Success = true,
                    Api = apiInfo,
                    TotalOperations = 0,
                    Operations = []
                };
            }

            var operations = new List<OperationInfo>();
            foreach (var block in operationBlocks)
            {
                var op = ParseOperationBlock(block);
                if (op != null)
                    operations.Add(op);
            }

            return new OperationsListResult
            {
                Success = true,
                Api = apiInfo,
                TotalOperations = operations.Count,
                Operations = operations
            };
        }
        catch (Exception ex)
        {
            return new OperationsListResult { Success = false, Error = $"Failed to parse Terraform: {ex.Message}" };
        }
    }

    // ── API-level extraction ────────────────────────────────────────────

    /// <summary>
    /// Extracts API metadata from the <c>api = [{ ... }]</c> block.
    /// </summary>
    internal static OperationsApiInfo ExtractApiInfo(string terraform)
    {
        // Find the api = [ ... ] section and extract the first block
        var apiBlock = ExtractFirstSectionBlock(terraform, "api");

        var name = ExtractFieldValue(apiBlock ?? terraform, "name");
        var displayName = ExtractFieldValue(apiBlock ?? terraform, "display_name");
        var path = ExtractFieldValue(apiBlock ?? terraform, "path");
        var serviceUrl = ExtractFieldValue(apiBlock ?? terraform, "service_url");

        // Try to detect environment from the content
        var env = EnvironmentTransformerService.DetectSourceEnvironment(terraform);

        return new OperationsApiInfo
        {
            Title = displayName ?? name ?? "Unknown",
            Name = name,
            Path = path,
            ServiceUrl = serviceUrl,
            Environment = env,
            Source = "terraform"
        };
    }

    /// <summary>
    /// Extracts the first block <c>{ ... }</c> inside a named section like <c>api = [</c>.
    /// Returns null if the section is not found.
    /// </summary>
    internal static string? ExtractFirstSectionBlock(string terraform, string sectionName)
    {
        var lines = terraform.Split('\n');
        var inSection = false;
        var braceDepth = 0;
        var inBlock = false;
        var block = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Match "api = [" but NOT "api_operations = [" or "api_name = "
            if (!inSection && Regex.IsMatch(trimmed, $@"^{Regex.Escape(sectionName)}\s*=\s*\["))
            {
                inSection = true;
                continue;
            }

            if (!inSection) continue;

            if (!inBlock && trimmed.StartsWith('{'))
            {
                inBlock = true;
                braceDepth = 0;
            }

            if (inBlock)
            {
                block.AppendLine(line);

                var inQuote = false;
                for (var i = 0; i < trimmed.Length; i++)
                {
                    var ch = trimmed[i];
                    if (ch == '"' && (i == 0 || trimmed[i - 1] != '\\'))
                    {
                        inQuote = !inQuote;
                        continue;
                    }
                    if (inQuote) continue;
                    if (ch == '{') braceDepth++;
                    else if (ch == '}') braceDepth--;
                }

                if (braceDepth == 0)
                    return block.ToString();
            }

            if (!inBlock && trimmed == "]")
                break;
        }

        return null;
    }

    // ── Operation block extraction ──────────────────────────────────────

    /// <summary>
    /// Extracts individual operation blocks from <c>api_operations = [...]</c>.
    /// Uses quote-aware brace counting (braces inside strings are ignored).
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
                currentBlock.Append(line).Append('\n');
                continue;
            }

            if (inBlock)
            {
                currentBlock.Append(line).Append('\n');

                var inQuote = false;
                for (var i = 0; i < trimmed.Length; i++)
                {
                    var ch = trimmed[i];
                    if (ch == '"' && (i == 0 || trimmed[i - 1] != '\\'))
                    {
                        inQuote = !inQuote;
                        continue;
                    }
                    if (inQuote) continue;
                    if (ch == '{') braceDepth++;
                    else if (ch == '}') braceDepth--;
                }

                if (braceDepth == 0)
                {
                    blocks.Add(currentBlock.ToString().TrimEnd());
                    inBlock = false;
                }
            }

            if (!inBlock && trimmed == "]")
                break;
        }

        return blocks;
    }

    // ── Single operation parsing ────────────────────────────────────────

    /// <summary>
    /// Parses a single operation block into a structured <see cref="OperationInfo"/>.
    /// </summary>
    internal static OperationInfo? ParseOperationBlock(string block)
    {
        var method = ExtractFieldValue(block, "method");
        var urlTemplate = ExtractFieldValue(block, "url_template");

        if (method == null || urlTemplate == null)
            return null;

        var operationId = ExtractFieldValue(block, "operation_id");
        var displayName = ExtractFieldValue(block, "display_name");
        var description = ExtractFieldValue(block, "description");
        var statusCode = ExtractFieldValue(block, "status_code");

        // Build parameters list: path params from url_template + header/query from request block
        var parameters = new List<ParameterInfo>();

        // 1. Path parameters from url_template placeholders
        var pathParams = Regex.Matches(urlTemplate, @"\{(\w+)\}");
        foreach (Match m in pathParams)
        {
            parameters.Add(new ParameterInfo
            {
                Name = m.Groups[1].Value,
                In = "path",
                Type = "string",
                Required = true
            });
        }

        // 2. Header and query parameters from request block
        parameters.AddRange(ExtractRequestParameters(block, "header"));
        parameters.AddRange(ExtractRequestParameters(block, "query_parameter"));

        // Request body content types from request > representation
        var requestContentTypes = ExtractRepresentationContentTypes(block, "request");

        // Response codes: collect from response sub-blocks + top-level status_code
        var responseCodes = ExtractResponseCodes(block);
        if (statusCode != null && int.TryParse(statusCode, out var topCode) && !responseCodes.Contains(topCode))
            responseCodes.Insert(0, topCode);

        // Use display_name as description if description is empty
        var desc = !string.IsNullOrWhiteSpace(description) ? description
                 : !string.IsNullOrWhiteSpace(displayName) ? displayName
                 : null;

        return new OperationInfo
        {
            Method = method.ToUpperInvariant(),
            UrlTemplate = urlTemplate,
            Path = "/" + urlTemplate,
            OperationId = operationId,
            Description = desc,
            Parameters = parameters.Count > 0 ? parameters : null,
            RequestBodyContentTypes = requestContentTypes.Count > 0 ? requestContentTypes : null,
            ResponseCodes = responseCodes.Count > 0 ? responseCodes : null
        };
    }

    // ── Request parameter extraction ────────────────────────────────────

    /// <summary>
    /// Extracts parameters from a named sub-block inside the <c>request</c> section.
    /// Parses blocks like <c>header = [{ name = "...", required = ..., type = "...", description = "..." }]</c>
    /// and <c>query_parameter = [{ ... }]</c>.
    /// </summary>
    internal static List<ParameterInfo> ExtractRequestParameters(string operationBlock, string paramKind)
    {
        var parameters = new List<ParameterInfo>();

        // Find the parameter list section within the request block
        var paramSection = ExtractNamedListSection(operationBlock, paramKind);
        if (paramSection == null) return parameters;

        // Extract individual parameter blocks
        var paramBlocks = ExtractNestedBlocks(paramSection);
        foreach (var pb in paramBlocks)
        {
            var name = ExtractFieldValue(pb, "name");
            if (name == null) continue;

            var type = ExtractFieldValue(pb, "type") ?? "string";
            var required = ExtractFieldBoolValue(pb, "required");
            var desc = ExtractFieldValue(pb, "description");

            var location = paramKind switch
            {
                "header" => "header",
                "query_parameter" => "query",
                _ => paramKind
            };

            parameters.Add(new ParameterInfo
            {
                Name = name,
                In = location,
                Type = type,
                Required = required,
                Description = string.IsNullOrWhiteSpace(desc) ? null : desc
            });
        }

        return parameters;
    }

    /// <summary>
    /// Extracts content types from representation blocks inside a request or response section.
    /// </summary>
    internal static List<string> ExtractRepresentationContentTypes(string operationBlock, string sectionName)
    {
        var contentTypes = new List<string>();

        var section = ExtractNamedListSection(operationBlock, sectionName);
        if (section == null) return contentTypes;

        var repSection = ExtractNamedListSection(section, "representation");
        if (repSection == null) return contentTypes;

        var repBlocks = ExtractNestedBlocks(repSection);
        foreach (var rb in repBlocks)
        {
            var ct = ExtractFieldValue(rb, "content_type");
            if (ct != null && !contentTypes.Contains(ct))
                contentTypes.Add(ct);
        }

        return contentTypes;
    }

    /// <summary>
    /// Extracts response status codes from <c>response = [{ status_code = 200 }, ...]</c> blocks.
    /// </summary>
    internal static List<int> ExtractResponseCodes(string operationBlock)
    {
        var codes = new List<int>();

        var responseSection = ExtractNamedListSection(operationBlock, "response");
        if (responseSection == null) return codes;

        var responseBlocks = ExtractNestedBlocks(responseSection);
        foreach (var rb in responseBlocks)
        {
            var codeStr = ExtractFieldValue(rb, "status_code");
            // status_code in response blocks may be unquoted integers
            if (codeStr == null)
            {
                var match = Regex.Match(rb, @"status_code\s*=\s*(\d+)");
                if (match.Success) codeStr = match.Groups[1].Value;
            }

            if (codeStr != null && int.TryParse(codeStr, out var code) && !codes.Contains(code))
                codes.Add(code);
        }

        return codes;
    }

    // ── Low-level HCL parsing helpers ───────────────────────────────────

    /// <summary>
    /// Extracts the first quoted string value of a named HCL field.
    /// Matches: <c>field_name = "value"</c> (with arbitrary whitespace).
    /// </summary>
    internal static string? ExtractFieldValue(string text, string fieldName)
    {
        var match = Regex.Match(text, $@"(?:^|\n)\s*{Regex.Escape(fieldName)}\s*=\s*""([^""]*?)""", RegexOptions.Multiline);
        return match.Success ? UnescapeHcl(match.Groups[1].Value) : null;
    }

    /// <summary>
    /// Extracts a boolean field value. Defaults to false if not found.
    /// </summary>
    internal static bool ExtractFieldBoolValue(string text, string fieldName)
    {
        var match = Regex.Match(text, $@"{Regex.Escape(fieldName)}\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        return match.Success && match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the content of a named list section: <c>name = [ ... ]</c>.
    /// Returns the text between (and including) the brackets.
    /// </summary>
    internal static string? ExtractNamedListSection(string text, string sectionName)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        var bracketDepth = 0;
        var inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!inSection)
            {
                // Match "sectionName = [" but be careful not to match partial names
                if (Regex.IsMatch(trimmed, $@"^{Regex.Escape(sectionName)}\s*=\s*\["))
                {
                    inSection = true;
                    bracketDepth = 1;
                    sb.Append(line).Append('\n');
                    // Check if closing bracket is on the same line
                    var inQuote = false;
                    foreach (var ch in trimmed)
                    {
                        if (ch == '"') inQuote = !inQuote;
                        if (inQuote) continue;
                        if (ch == '[') bracketDepth++;
                        else if (ch == ']') bracketDepth--;
                    }
                    // Subtract 1 because we already counted the opening [
                    bracketDepth = CountBracketDepth(trimmed);
                    if (bracketDepth == 0)
                        return sb.ToString();
                    continue;
                }
                continue;
            }

            sb.Append(line).Append('\n');

            bracketDepth += CountBracketDepthDelta(trimmed);

            if (bracketDepth == 0)
                return sb.ToString();
        }

        return inSection ? sb.ToString() : null;
    }

    /// <summary>
    /// Extracts all top-level <c>{ ... }</c> blocks from a section of text.
    /// </summary>
    internal static List<string> ExtractNestedBlocks(string sectionText)
    {
        var blocks = new List<string>();
        var lines = sectionText.Split('\n');
        var braceDepth = 0;
        var inBlock = false;
        var block = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!inBlock && trimmed.StartsWith('{'))
            {
                inBlock = true;
                braceDepth = 0;
            }

            if (!inBlock) continue;

            block.Append(line).Append('\n');

            var inQuote = false;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (ch == '"' && (i == 0 || trimmed[i - 1] != '\\'))
                {
                    inQuote = !inQuote;
                    continue;
                }
                if (inQuote) continue;
                if (ch == '{') braceDepth++;
                else if (ch == '}') braceDepth--;
            }

            if (braceDepth == 0)
            {
                blocks.Add(block.ToString().TrimEnd());
                block.Clear();
                inBlock = false;
            }
        }

        return blocks;
    }

    /// <summary>
    /// Counts the net bracket depth of a line ([ increments, ] decrements),
    /// ignoring brackets inside quoted strings.
    /// </summary>
    private static int CountBracketDepth(string line)
    {
        var depth = 0;
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inQuote = !inQuote;
                continue;
            }
            if (inQuote) continue;
            if (ch == '[') depth++;
            else if (ch == ']') depth--;
        }
        return depth;
    }

    /// <summary>
    /// Returns the net change in bracket depth for a single line.
    /// </summary>
    private static int CountBracketDepthDelta(string line)
    {
        return CountBracketDepth(line);
    }

    private static string UnescapeHcl(string value) =>
        value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n");
}
