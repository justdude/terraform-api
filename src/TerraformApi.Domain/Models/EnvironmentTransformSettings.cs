namespace TerraformApi.Domain.Models;

/// <summary>
/// Settings for transforming a Terraform configuration from one APIM environment to another.
/// The source environment is auto-detected from the Terraform content if not explicitly provided.
/// </summary>
public sealed record EnvironmentTransformSettings
{
    /// <summary>Source environment name (e.g. "dev"). Auto-detected from Terraform if null.</summary>
    public string? SourceEnvironment { get; init; }

    /// <summary>Target environment name (e.g. "staging", "prod"). Required.</summary>
    public required string TargetEnvironment { get; init; }

    /// <summary>Target Azure resource group for APIM (e.g. "rg-apim-staging").</summary>
    public required string TargetStageGroupName { get; init; }

    /// <summary>Target APIM instance name (e.g. "apim-company-staging").</summary>
    public required string TargetApimName { get; init; }

    /// <summary>Target API gateway hostname (e.g. "api-staging.company.com").</summary>
    public required string TargetApiGatewayHost { get; init; }

    /// <summary>Target frontend host for CORS origins (e.g. "portal").</summary>
    public string? TargetFrontendHost { get; init; }

    /// <summary>Target company domain for CORS origins (e.g. "company.com").</summary>
    public string? TargetCompanyDomain { get; init; }

    /// <summary>Target local dev host for CORS (e.g. "localhost"). Null to remove.</summary>
    public string? TargetLocalDevHost { get; init; }

    /// <summary>Target local dev port for CORS (e.g. "3000"). Null to remove.</summary>
    public string? TargetLocalDevPort { get; init; }

    /// <summary>Override subscription_required for the target. Null to keep source value.</summary>
    public bool? TargetSubscriptionRequired { get; init; }
}
