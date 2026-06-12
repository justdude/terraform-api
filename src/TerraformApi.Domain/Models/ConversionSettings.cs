namespace TerraformApi.Domain.Models;

/// <summary>
/// APIM conversion settings. Every identity/routing setting is optional:
/// values the caller does not provide are normalized to placeholder tags
/// (see <see cref="ApimPlaceholders"/>) so generation never blocks — the user
/// replaces the tags in the output later.
/// </summary>
public sealed record ConversionSettings
{
    public string? Environment { get; init; }
    public string? ApiGroupName { get; init; }
    public string? StageGroupName { get; init; }
    public string? ApimName { get; init; }
    public string? ApiName { get; init; }
    public string? ApiDisplayName { get; init; }
    public string? ApiPathPrefix { get; init; }
    public string? ApiPathSuffix { get; init; }
    public string? ApiGatewayHost { get; init; }
    public string ApiVersion { get; init; } = "v1";
    public string? BackendServicePath { get; init; }
    public string Revision { get; init; } = "1";
    public string? ProductId { get; init; }
    public string? FrontendHost { get; init; }
    public string? CompanyDomain { get; init; }
    public string? LocalDevHost { get; init; }
    public string? LocalDevPort { get; init; }
    public bool SubscriptionRequired { get; init; }
    public bool IncludeCorsPolicy { get; init; }
    public string? OperationPrefix { get; init; }
    public List<string> AllowedOrigins { get; init; } = [];
    public List<string> AllowedMethods { get; init; } = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];

    // Product generation
    public bool GenerateProduct { get; init; }
    public string? ProductDisplayName { get; init; }
    public string? ProductDescription { get; init; }
    public bool ProductSubscriptionRequired { get; init; }
    public bool ProductApprovalRequired { get; init; }
}
