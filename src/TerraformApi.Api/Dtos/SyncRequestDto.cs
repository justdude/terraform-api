namespace TerraformApi.Api.Dtos;

/// <summary>
/// Request DTO for <c>POST /api/sync</c> — append-only synchronization of an
/// existing Terraform config with an OpenAPI specification.
/// Inherits all APIM settings from <see cref="ConvertRequest"/>.
/// </summary>
public class SyncRequestDto : ConvertRequest
{
    /// <summary>Existing Terraform HCL. Null/empty → generate from scratch.</summary>
    public string? ExistingTerraform { get; init; }

    /// <summary>
    /// Template profile for new operations:
    /// "Auto" (default — detect from the existing file), "UserExampleProfile",
    /// "ExtendedProfile", or "LiteralProfile".
    /// </summary>
    public string? TemplateProfileName { get; init; }

    /// <summary>Variable values for resolved-mode matching (e.g. {"env":"dev"}).</summary>
    public Dictionary<string, string>? VariableContext { get; init; }

    /// <summary>
    /// Per-field policy overrides for matched operations:
    /// field name → "Preserve" | "EnrichIfMissing" | "Overwrite".
    /// </summary>
    public Dictionary<string, string>? OperationFieldOverrides { get; init; }

    /// <summary>
    /// Match key order: any of "OperationId", "MethodAndUrl",
    /// "MethodAndUrlAndParams", "Tag", "ApiAndMethodAndUrl", "RgApiAndMethodAndUrl".
    /// Default: ["MethodAndUrl", "OperationId", "Tag"].
    /// </summary>
    public List<string>? MatchKeys { get; init; }

    public bool AddOperationComments { get; init; } = true;
    public bool AddReplaceBeforeApplyHeader { get; init; } = true;
}

/// <summary>Request DTO for <c>POST /api/analyze-terraform</c>.</summary>
public sealed record AnalyzeTerraformRequest
{
    /// <summary>The Terraform HCL to analyze.</summary>
    public required string Terraform { get; init; }
}

/// <summary>Request DTO for <c>POST /api/apply-template-profile</c>.</summary>
public sealed record ApplyTemplateProfileRequest
{
    public required string ExistingTerraform { get; init; }

    /// <summary>"Templatize" (literals → templates) or "Resolve" (templates → literals).</summary>
    public required string Direction { get; init; }

    /// <summary>Profile name; required for Templatize.</summary>
    public string? ProfileName { get; init; }

    /// <summary>Variable values; required for Resolve.</summary>
    public Dictionary<string, string>? VariableContext { get; init; }

    /// <summary>Overwrite literals that already have a value (Templatize only).</summary>
    public bool OverwriteExisting { get; init; }
}
