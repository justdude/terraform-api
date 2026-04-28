using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that converts an OpenAPI JSON specification into an Azure APIM Terraform configuration.
/// </summary>
[McpServerToolType]
public static class ConvertTool
{
    [McpServerTool(Name = "convert_openapi_to_terraform")]
    [Description("Converts an OpenAPI JSON specification into an Azure APIM Terraform configuration. " +
                 "Provide the OpenAPI JSON content and APIM settings (environment, resource group, APIM instance name, etc.) " +
                 "to generate a complete Terraform HCL block for Azure API Management.")]
    public static string Convert(
        IConversionOrchestrator orchestrator,
        [Description("The OpenAPI specification JSON string (OpenAPI 3.x format)")] string openApiJson,
        [Description("Target environment name (e.g. 'dev', 'staging', 'prod')")] string environment,
        [Description("Terraform variable group name for the API (e.g. 'my-api-group')")] string apiGroupName,
        [Description("Azure resource group name for the APIM instance (e.g. 'rg-apim-dev')")] string stageGroupName,
        [Description("Azure APIM instance name (e.g. 'apim-company-dev')")] string apimName,
        [Description("Path prefix for the API (e.g. 'myapp')")] string apiPathPrefix,
        [Description("Path suffix for the API (e.g. 'api')")] string apiPathSuffix,
        [Description("API gateway hostname (e.g. 'api.dev.company.com')")] string apiGatewayHost,
        [Description("Backend service path segment (e.g. 'my-service')")] string backendServicePath,
        [Description("Optional: override the auto-generated API name")] string? apiName = null,
        [Description("Optional: override the display name (defaults to OpenAPI info.title)")] string? apiDisplayName = null,
        [Description("API version string (default: 'v1')")] string apiVersion = "v1",
        [Description("APIM revision number (default: '1')")] string revision = "1",
        [Description("Whether a subscription key is required (default: false)")] bool subscriptionRequired = false,
        [Description("Whether to generate a CORS policy block (default: false)")] bool includeCorsPolicy = false,
        [Description("Frontend subdomain for CORS origins (e.g. 'portal')")] string? frontendHost = null,
        [Description("Company domain for CORS origin construction (e.g. 'company.com')")] string? companyDomain = null,
        [Description("Local dev host for CORS (e.g. 'localhost')")] string? localDevHost = null,
        [Description("Local dev port for CORS (e.g. '3000')")] string? localDevPort = null,
        [Description("APIM product ID to associate the API with")] string? productId = null,
        [Description("Whether to generate a product block (default: false)")] bool generateProduct = false,
        [Description("Product display name")] string? productDisplayName = null,
        [Description("Product description")] string? productDescription = null,
        [Description("Whether the product requires a subscription (default: false)")] bool productSubscriptionRequired = false,
        [Description("Whether the product requires approval (default: false)")] bool productApprovalRequired = false)
    {
        var settings = new ConversionSettings
        {
            Environment = environment,
            ApiGroupName = apiGroupName,
            StageGroupName = stageGroupName,
            ApimName = apimName,
            ApiPathPrefix = apiPathPrefix,
            ApiPathSuffix = apiPathSuffix,
            ApiGatewayHost = apiGatewayHost,
            BackendServicePath = backendServicePath,
            ApiName = apiName,
            ApiDisplayName = apiDisplayName,
            ApiVersion = apiVersion,
            Revision = revision,
            SubscriptionRequired = subscriptionRequired,
            IncludeCorsPolicy = includeCorsPolicy,
            FrontendHost = frontendHost,
            CompanyDomain = companyDomain,
            LocalDevHost = localDevHost,
            LocalDevPort = localDevPort,
            ProductId = productId,
            GenerateProduct = generateProduct,
            ProductDisplayName = productDisplayName,
            ProductDescription = productDescription,
            ProductSubscriptionRequired = productSubscriptionRequired,
            ProductApprovalRequired = productApprovalRequired
        };

        var result = orchestrator.Convert(openApiJson, settings);

        if (result.Success)
        {
            var output = result.TerraformConfig;

            if (result.Warnings.Count > 0)
                output += "\n\n// Warnings:\n" + string.Join("\n", result.Warnings.Select(w => $"// - {w}"));

            if (result.Configuration is not null)
            {
                output += $"\n\n// Summary: {result.Configuration.Api.Name} " +
                          $"({result.Configuration.ApiOperations.Count} operations)";
            }

            return output;
        }

        return "Conversion failed:\n" + string.Join("\n", result.Errors);
    }
}
