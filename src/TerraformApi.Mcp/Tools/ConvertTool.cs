using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that converts an OpenAPI JSON specification into an Azure APIM Terraform configuration.
/// Supports both direct JSON input and fetching from URLs.
/// </summary>
[McpServerToolType]
public static class ConvertTool
{
    [McpServerTool(Name = "convert_openapi_to_terraform")]
    [Description("Converts an OpenAPI JSON specification into an Azure APIM Terraform configuration. " +
                 "You can provide either the OpenAPI JSON content directly or specify a URL to fetch it from. " +
                 "Include APIM settings (environment, resource group, APIM instance name, etc.) to generate " +
                 "a complete Terraform HCL block for Azure API Management. " +
                 "Optionally pass templateProfileName to generate templated output with ${...} placeholders " +
                 "(UserExampleProfile/ExtendedProfile/LiteralProfile), operation comments and a placeholder header.")]
    public static async Task<string> Convert(
        HttpClient httpClient,
        IConversionOrchestrator orchestrator,
        IOpenApiParser openApiParser,
        IApimTerraformWriter apimWriter,
        [Description("Target environment name (e.g. 'dev', 'staging', 'prod')")] string environment,
        [Description("Terraform variable group name for the API (e.g. 'my-api-group')")] string apiGroupName,
        [Description("Azure resource group name for the APIM instance (e.g. 'rg-apim-dev')")] string stageGroupName,
        [Description("Azure APIM instance name (e.g. 'apim-company-dev')")] string apimName,
        [Description("Path prefix for the API (e.g. 'myapp')")] string apiPathPrefix,
        [Description("Path suffix for the API (e.g. 'api')")] string apiPathSuffix,
        [Description("API gateway hostname (e.g. 'api.dev.company.com')")] string apiGatewayHost,
        [Description("Backend service path segment (e.g. 'my-service')")] string backendServicePath,
        [Description("The OpenAPI specification JSON string (OpenAPI 3.x format). Leave empty if providing openApiUrl instead.")] string? openApiJson = null,
        [Description("URL to fetch the OpenAPI specification from (e.g., https://api.example.com/openapi.json). Used if openApiJson is not provided.")] string? openApiUrl = null,
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
        [Description("Optional prefix for generated operation IDs")] string? operationPrefix = null,
        [Description("Additional CORS allowed origins (list of URLs)")] List<string>? allowedOrigins = null,
        [Description("CORS allowed HTTP methods (defaults to GET, POST, PUT, DELETE, OPTIONS)")] List<string>? allowedMethods = null,
        [Description("APIM product ID to associate the API with")] string? productId = null,
        [Description("Whether to generate a product block (default: false)")] bool generateProduct = false,
        [Description("Product display name")] string? productDisplayName = null,
        [Description("Product description")] string? productDescription = null,
        [Description("Whether the product requires a subscription (default: false)")] bool productSubscriptionRequired = false,
        [Description("Whether the product requires approval (default: false)")] bool productApprovalRequired = false,
        [Description("Optional template profile for templated output: 'UserExampleProfile', 'ExtendedProfile' or 'LiteralProfile'. " +
                     "Omit for classic literal generation.")] string? templateProfileName = null,
        [Description("Add a descriptive comment block above each operation (templated output only, default: true)")] bool addOperationComments = true,
        [Description("Add the REPLACE BEFORE APPLY placeholder header (templated output only, default: true)")] bool addReplaceBeforeApplyHeader = true,
        [Description("Wrapper structure as JSON array (templated output only), e.g. [\"apis\",\"bpc_apis\",\"backend_apis\"] (the default) or [] for flat")] string? apiGroupParentPathJson = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedJson = await ResolveOpenApiJson(httpClient, openApiJson, openApiUrl, cancellationToken);

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
                OperationPrefix = operationPrefix,
                AllowedOrigins = allowedOrigins ?? [],
                AllowedMethods = allowedMethods ?? ["GET", "POST", "PUT", "DELETE", "OPTIONS"],
                ProductId = productId,
                GenerateProduct = generateProduct,
                ProductDisplayName = productDisplayName,
                ProductDescription = productDescription,
                ProductSubscriptionRequired = productSubscriptionRequired,
                ProductApprovalRequired = productApprovalRequired
            };

            // Templated output path: build the AST through the template profile.
            if (!string.IsNullOrWhiteSpace(templateProfileName))
            {
                var profile = Domain.Models.Sync.ApimTemplateProfile.GetByName(templateProfileName);
                if (profile is null)
                    return $"Conversion error: unknown template profile '{templateProfileName}'. " +
                           "Available: UserExampleProfile, ExtendedProfile, LiteralProfile.";

                IReadOnlyList<string>? parentPath = null;
                if (!string.IsNullOrWhiteSpace(apiGroupParentPathJson))
                    parentPath = JsonSerializer.Deserialize<List<string>>(apiGroupParentPathJson);

                var configuration = openApiParser.Parse(resolvedJson, settings);
                var buildOptions = new Domain.Models.Sync.BuildOptions
                {
                    Profile = profile,
                    ApiGroupParentPath = parentPath ?? new Domain.Models.Sync.BuildOptions().ApiGroupParentPath,
                    AddOperationComments = addOperationComments,
                    AddReplaceBeforeApplyHeader = addReplaceBeforeApplyHeader
                };

                var document = apimWriter.BuildFromConfiguration(configuration, buildOptions);
                return apimWriter.Write(document);
            }

            var result = orchestrator.Convert(resolvedJson, settings);

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
        catch (Exception ex)
        {
            return $"Conversion error: {ex.Message}";
        }
    }

    /// <summary>
    /// Resolves the OpenAPI specification JSON from either direct input or a URL.
    /// Delegates to the shared <see cref="Application.Services.OpenApiDocumentResolver"/>
    /// so the MCP server and the HTTP API validate input identically.
    /// </summary>
    internal static Task<string> ResolveOpenApiJson(
        HttpClient httpClient, string? openApiJson, string? openApiUrl, CancellationToken cancellationToken = default) =>
        Application.Services.OpenApiDocumentResolver.ResolveAsync(httpClient, openApiJson, openApiUrl, cancellationToken);
}
