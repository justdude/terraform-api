using System.ComponentModel.DataAnnotations;

namespace TerraformApi.Api.Dtos;

public class ConvertRequest
{
    [Required]
    public required string OpenApiJson { get; init; }

    [Required]
    public required string Environment { get; init; }

    [Required]
    public required string ApiGroupName { get; init; }

    [Required]
    public required string StageGroupName { get; init; }

    [Required]
    public required string ApimName { get; init; }

    public string? ApiName { get; init; }
    public string? ApiDisplayName { get; init; }

    [Required]
    public required string ApiPathPrefix { get; init; }

    [Required]
    public required string ApiPathSuffix { get; init; }

    [Required]
    public required string ApiGatewayHost { get; init; }

    public string ApiVersion { get; init; } = "v1";

    [Required]
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
