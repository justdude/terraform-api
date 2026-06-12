using System.ComponentModel.DataAnnotations;

namespace TerraformApi.Api.Dtos;

public class ConvertRequest
{
    /// <summary>
    /// The OpenAPI specification JSON string. Required unless OpenApiUrl is provided.
    /// </summary>
    public string? OpenApiJson { get; init; }

    /// <summary>
    /// URL to fetch the OpenAPI specification from. Used if OpenApiJson is not provided.
    /// </summary>
    public string? OpenApiUrl { get; init; }

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
