using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool for one-time conversion between literal and templated styles:
/// Templatize replaces literals with profile placeholders; Resolve substitutes
/// variable values into placeholders.
/// </summary>
[McpServerToolType]
public static class ApplyTemplateProfileTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool(Name = "apply_template_profile")]
    [Description("One-time conversion of an APIM Terraform config between literal and templated styles. " +
                 "Direction 'Templatize': replaces literal field values with the profile's ${...} placeholders " +
                 "(by default only empty/missing fields; set overwriteExisting=true to replace all). " +
                 "Direction 'Resolve': substitutes variable values into ${...} placeholders, producing " +
                 "literal HCL for a specific environment. " +
                 "NOTE: unlike sync, this MAY change values — use deliberately.")]
    public static string Apply(
        ISyncOrchestrator orchestrator,
        [Description("The existing Terraform HCL configuration")] string existingTerraform,
        [Description("'Templatize' (literals → templates) or 'Resolve' (templates → literals)")] string direction,
        [Description("Profile to apply (Templatize only): 'UserExampleProfile', 'ExtendedProfile', or 'LiteralProfile'")] string? profileName = null,
        [Description("Variable values as JSON (Resolve only), e.g. {\"env\":\"dev\",\"apim_name\":\"apim-company-dev\"}")] string? variableContextJson = null,
        [Description("Overwrite literals that already have a value (default: false — only fill empty/missing)")] bool overwriteExisting = false)
    {
        try
        {
            var resolve = direction.Equals("Resolve", StringComparison.OrdinalIgnoreCase);
            if (!resolve && !direction.Equals("Templatize", StringComparison.OrdinalIgnoreCase))
                return Error("Direction must be 'Templatize' or 'Resolve'.");

            ApimTemplateProfile? profile = null;
            if (!resolve)
            {
                profile = ApimTemplateProfile.GetByName(profileName ?? "");
                if (profile is null)
                    return Error($"Unknown template profile '{profileName}'. Available: UserExampleProfile, ExtendedProfile, LiteralProfile.");
            }

            Dictionary<string, string>? variables = null;
            if (!string.IsNullOrWhiteSpace(variableContextJson))
                variables = JsonSerializer.Deserialize<Dictionary<string, string>>(variableContextJson, JsonOptions);

            var result = orchestrator.ApplyProfile(
                existingTerraform,
                profile,
                new ApplyProfileOptions { OverwriteExisting = overwriteExisting },
                variables,
                resolve);

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error($"Invalid JSON parameter: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Error($"Apply profile error: {ex.Message}");
        }
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
