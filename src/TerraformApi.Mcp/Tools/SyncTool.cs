using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool for append-only synchronization of existing Terraform with an
/// OpenAPI spec — the main sync command. Never deletes anything.
/// </summary>
[McpServerToolType]
public static class SyncTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [McpServerTool(Name = "sync_openapi_with_terraform")]
    [Description("Append-only sync of an existing APIM Terraform config with an OpenAPI spec. " +
                 "Operations missing from Terraform are appended (in the file's detected templating style, " +
                 "with descriptive leading comments); operations present in both are enriched only where " +
                 "fields are missing; operations only in Terraform are always preserved. Nothing is deleted. " +
                 "Empty existingTerraform generates a fresh config. " +
                 "Returns the final HCL plus a full sync report (added/preserved/enriched/identical, duplicates, warnings).")]
    public static async Task<string> Sync(
        HttpClient httpClient,
        ISyncOrchestrator orchestrator,
        [Description("The existing Terraform HCL configuration. Empty string generates from scratch.")] string existingTerraform,
        [Description("Target environment name (e.g. 'dev', 'staging', 'prod')")] string environment,
        [Description("Terraform variable group name for the API (e.g. 'my-api-group' or '${api_group_name}')")] string apiGroupName,
        [Description("Azure resource group name for the APIM instance (e.g. 'rg-apim-dev')")] string stageGroupName,
        [Description("Azure APIM instance name (e.g. 'apim-company-dev')")] string apimName,
        [Description("Path prefix for the API (e.g. 'myapp')")] string apiPathPrefix,
        [Description("Path suffix for the API (e.g. 'api')")] string apiPathSuffix,
        [Description("API gateway hostname (e.g. 'api.dev.company.com')")] string apiGatewayHost,
        [Description("Backend service path segment (e.g. 'my-service')")] string backendServicePath,
        [Description("The OpenAPI specification JSON string (OpenAPI 3.x format). Leave empty if providing openApiUrl instead.")] string? openApiJson = null,
        [Description("URL to fetch the OpenAPI specification from. Used if openApiJson is not provided.")] string? openApiUrl = null,
        [Description("Template profile for new operations: 'Auto' (default — detect from existing file), " +
                     "'UserExampleProfile', 'ExtendedProfile', or 'LiteralProfile'")] string templateProfileName = "Auto",
        [Description("Per-field policy overrides as JSON, e.g. {\"description\":\"Overwrite\"}. " +
                     "Values: Preserve | EnrichIfMissing | Overwrite")] string? operationFieldOverridesJson = null,
        [Description("Match key order as JSON array, e.g. [\"MethodAndUrl\",\"OperationId\"]")] string? matchKeysJson = null,
        [Description("Variable values for resolved-mode matching as JSON, e.g. {\"env\":\"dev\",\"api_name\":\"bpc\"}")] string? variableContextJson = null,
        [Description("Add a descriptive comment block above each inserted operation (default: true)")] bool addOperationComments = true,
        [Description("Add the REPLACE BEFORE APPLY placeholder header (default: true)")] bool addReplaceBeforeApplyHeader = true,
        [Description("Optional: override the auto-generated API name")] string? apiName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedJson = await ConvertTool.ResolveOpenApiJson(httpClient, openApiJson, openApiUrl, cancellationToken);

            ApimTemplateProfile? overrideProfile = null;
            if (templateProfileName is { Length: > 0 } && templateProfileName != "Auto")
            {
                overrideProfile = ApimTemplateProfile.GetByName(templateProfileName);
                if (overrideProfile is null)
                    return Error($"Unknown template profile '{templateProfileName}'. Available: Auto, UserExampleProfile, ExtendedProfile, LiteralProfile.");
            }

            MergePolicy? policy = null;
            if (!string.IsNullOrWhiteSpace(operationFieldOverridesJson))
            {
                var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(operationFieldOverridesJson, JsonOptions);
                policy = new MergePolicy();
                foreach (var (field, value) in overrides ?? [])
                {
                    if (!Enum.TryParse<FieldMergePolicy>(value, ignoreCase: true, out var fieldPolicy))
                        return Error($"Unknown field policy '{value}' for field '{field}'.");
                    policy = policy.WithOverride(field, fieldPolicy);
                }
            }

            Dictionary<string, string>? variableContext = null;
            if (!string.IsNullOrWhiteSpace(variableContextJson))
                variableContext = JsonSerializer.Deserialize<Dictionary<string, string>>(variableContextJson, JsonOptions);

            OperationMatchStrategy? strategy = null;
            if (!string.IsNullOrWhiteSpace(matchKeysJson) || variableContext is { Count: > 0 })
            {
                var keys = new List<OperationMatchKey>();
                var keyNames = string.IsNullOrWhiteSpace(matchKeysJson)
                    ? ["MethodAndUrl", "OperationId", "Tag"]
                    : JsonSerializer.Deserialize<List<string>>(matchKeysJson, JsonOptions) ?? [];

                foreach (var keyName in keyNames)
                {
                    if (!Enum.TryParse<OperationMatchKey>(keyName, ignoreCase: true, out var key))
                        return Error($"Unknown match key '{keyName}'.");
                    keys.Add(key);
                }

                strategy = new OperationMatchStrategy { Keys = keys, VariableContext = variableContext };
            }

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
                ApiName = apiName
            };

            var result = orchestrator.Sync(new SyncRequest
            {
                OpenApiJson = resolvedJson,
                ExistingTerraform = existingTerraform,
                Settings = settings,
                MergePolicy = policy,
                MatchStrategy = strategy,
                Options = new SyncOptions
                {
                    OverrideProfile = overrideProfile,
                    AddOperationComments = addOperationComments,
                    AddReplaceBeforeApplyHeader = addReplaceBeforeApplyHeader
                }
            });

            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Error($"Invalid JSON parameter: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Error($"Sync error: {ex.Message}");
        }
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
