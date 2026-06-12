namespace TerraformApi.Api.Dtos;

/// <summary>
/// APIM conversion settings. All identity/routing settings are optional —
/// anything not provided is generated with a placeholder tag (e.g. {api-group})
/// that the caller replaces later; the generated file starts with a comment
/// explaining every tag used.
/// </summary>
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

    /// <summary>Target environment. Defaults to the {environment} tag.</summary>
    public string? Environment { get; init; }

    /// <summary>Terraform variable group name. Defaults to the {api-group} tag.</summary>
    public string? ApiGroupName { get; init; }

    /// <summary>APIM resource group. Defaults to the {stage-group-name} tag.</summary>
    public string? StageGroupName { get; init; }

    /// <summary>APIM instance name. Defaults to the {apim-name} tag.</summary>
    public string? ApimName { get; init; }

    public string? ApiName { get; init; }
    public string? ApiDisplayName { get; init; }

    /// <summary>API path prefix. Defaults to the {api-path-prefix} tag.</summary>
    public string? ApiPathPrefix { get; init; }

    /// <summary>API path suffix. Defaults to the {api-path-suffix} tag.</summary>
    public string? ApiPathSuffix { get; init; }

    /// <summary>Gateway hostname. Defaults to the {api-gateway-host} tag.</summary>
    public string? ApiGatewayHost { get; init; }

    public string ApiVersion { get; init; } = "v1";

    /// <summary>Backend service path. Defaults to the {backend-service-path} tag.</summary>
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
