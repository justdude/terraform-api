namespace TerraformApi.Domain.Models;

public sealed record ConversionSettings
{
    public required string Environment { get; init; }
    public required string ApiGroupName { get; init; }
    public required string StageGroupName { get; init; }
    public required string ApimName { get; init; }
    public string? ApiName { get; init; }
    public string? ApiDisplayName { get; init; }
    public required string ApiPathPrefix { get; init; }
    public required string ApiPathSuffix { get; init; }
    public required string ApiGatewayHost { get; init; }
    public string ApiVersion { get; init; } = "v1";
    public required string BackendServicePath { get; init; }
    public string Revision { get; init; } = "1";
    public string? ProductId { get; init; }
    public string? FrontendHost { get; init; }
    public string? CompanyDomain { get; init; }
    public string? LocalDevHost { get; init; }
    public string? LocalDevPort { get; init; }
    public bool SubscriptionRequired { get; init; }
    public bool IncludeCorsPolicy { get; init; } = true;
    public string? OperationPrefix { get; init; }
    public List<string> AllowedOrigins { get; init; } = [];
    public List<string> AllowedMethods { get; init; } = ["GET", "POST", "PUT", "DELETE", "OPTIONS"];
}
