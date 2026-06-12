using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that parses a Terraform APIM configuration and returns a structured list
/// of all operations — using the same unified format as <c>fetch_openapi_operations</c>.
/// This enables side-by-side comparison between what an OpenAPI spec defines
/// and what's actually deployed in Terraform.
/// </summary>
[McpServerToolType]
public static class ParseTerraformOperationsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool(Name = "parse_terraform_operations")]
    [Description("Parses a Terraform APIM configuration (HCL text) and returns a structured list of all API operations. " +
                 "For each operation returns: HTTP method, URL path, operationId, parameters (name, type, location, required), " +
                 "description, request body content types, and response codes. " +
                 "Output format matches fetch_openapi_operations so you can compare OpenAPI specs vs Terraform configs side-by-side.")]
    public static string ParseTerraformOperations(
        ITerraformOperationsParser parser,
        [Description("The Terraform APIM configuration HCL text to parse (the content of your .tf or terraform variable file)")] string terraform)
    {
        return ParseOperationsCore(parser, terraform);
    }

    /// <summary>
    /// Core implementation separated for testability.
    /// </summary>
    internal static string ParseOperationsCore(ITerraformOperationsParser parser, string terraform)
    {
        if (string.IsNullOrWhiteSpace(terraform))
            return FormatError("Terraform content is required.");

        var result = parser.Parse(terraform);

        if (!result.Success)
            return FormatError(result.Error ?? "Failed to parse Terraform.");

        // Serialize the same unified domain model used by FetchOperationsTool
        return JsonSerializer.Serialize(new
        {
            api = result.Api,
            totalOperations = result.TotalOperations,
            operations = result.Operations
        }, JsonOptions);
    }

    private static string FormatError(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions);
}
