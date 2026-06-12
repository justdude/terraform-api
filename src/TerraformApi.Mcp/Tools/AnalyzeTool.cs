using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that analyzes an existing APIM Terraform config without modifying it:
/// API groups, operation counts, detected templating style, referenced variables,
/// and duplicate operations.
/// </summary>
[McpServerToolType]
public static class AnalyzeTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool(Name = "analyze_terraform_apim")]
    [Description("Analyzes an existing APIM Terraform configuration (HCL) without modifying it. " +
                 "Returns: API groups by (resource group, api name) with operation counts, " +
                 "the detected templating profile (HighlyTemplated/Mixed/MostlyLiteral) with per-field statistics, " +
                 "all referenced ${...} variables, and any duplicate operations. " +
                 "Use this first to understand a file before syncing.")]
    public static string Analyze(
        ISyncOrchestrator orchestrator,
        [Description("The Terraform APIM configuration HCL text to analyze")] string existingTerraform)
    {
        return AnalyzeCore(orchestrator, existingTerraform);
    }

    /// <summary>Core implementation separated for testability.</summary>
    internal static string AnalyzeCore(ISyncOrchestrator orchestrator, string existingTerraform)
    {
        var result = orchestrator.Analyze(existingTerraform);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
