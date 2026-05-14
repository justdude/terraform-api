using System.ComponentModel.DataAnnotations;

namespace TerraformApi.Api.Dtos;

/// <summary>
/// Request body for the cross-environment Terraform transform endpoint.
/// Transforms a source environment's Terraform to a target environment,
/// optionally merging with an existing target configuration.
/// </summary>
public class TransformRequest
{
    /// <summary>The source environment's Terraform HCL configuration.</summary>
    [Required]
    public required string SourceTerraform { get; init; }

    /// <summary>Source environment name. Auto-detected from Terraform if omitted.</summary>
    public string? SourceEnvironment { get; init; }

    /// <summary>Target environment name (e.g. "staging", "prod").</summary>
    [Required]
    public required string TargetEnvironment { get; init; }

    /// <summary>Target Azure resource group for APIM (e.g. "rg-apim-staging").</summary>
    [Required]
    public required string TargetStageGroupName { get; init; }

    /// <summary>Target APIM instance name (e.g. "apim-company-staging").</summary>
    [Required]
    public required string TargetApimName { get; init; }

    /// <summary>Target API gateway hostname (e.g. "api-staging.company.com").</summary>
    [Required]
    public required string TargetApiGatewayHost { get; init; }

    /// <summary>Target frontend host for CORS origins.</summary>
    public string? TargetFrontendHost { get; init; }

    /// <summary>Target company domain for CORS origins.</summary>
    public string? TargetCompanyDomain { get; init; }

    /// <summary>Target local dev host for CORS. Null to keep source value.</summary>
    public string? TargetLocalDevHost { get; init; }

    /// <summary>Target local dev port for CORS. Null to keep source value.</summary>
    public string? TargetLocalDevPort { get; init; }

    /// <summary>Override subscription_required for the target. Null to keep source value.</summary>
    public bool? TargetSubscriptionRequired { get; init; }

    /// <summary>
    /// Optional existing target environment's Terraform HCL for merge/sync.
    /// When provided, operations are matched by url_template + method:
    /// source operations sync into target, target-only operations are preserved.
    /// </summary>
    public string? ExistingTargetTerraform { get; init; }
}
