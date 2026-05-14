using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that transforms a Terraform APIM configuration from one environment to another.
/// Supports passing both a source (e.g. dev) and an existing target (e.g. staging) Terraform
/// to sync operations between environments. Operations are matched by url_template + HTTP method,
/// not by operation_id (since IDs typically differ across environments).
/// </summary>
[McpServerToolType]
public static class TransformEnvironmentTool
{
    [McpServerTool(Name = "transform_environment")]
    [Description("Transforms a Terraform APIM configuration from one environment to another. " +
                 "Pass the source environment's Terraform (e.g. dev) and target environment settings " +
                 "to generate the equivalent config for staging/prod/etc. Optionally provide the " +
                 "existing target environment's Terraform to sync: operations are matched by " +
                 "url_template + HTTP method (not operation_id), source operations are synced, " +
                 "and target-only operations are preserved. Use 'list_environment_presets' first " +
                 "to get pre-configured target values.")]
    public static string Transform(
        IEnvironmentTransformer transformer,
        [Description("The source environment's Terraform HCL configuration (e.g. the dev config)")] string sourceTerraform,
        [Description("Target environment name (e.g. 'staging', 'prod')")] string targetEnvironment,
        [Description("Target Azure resource group for APIM (e.g. 'rg-apim-staging')")] string targetStageGroupName,
        [Description("Target APIM instance name (e.g. 'apim-company-staging')")] string targetApimName,
        [Description("Target API gateway hostname (e.g. 'api-staging.company.com')")] string targetApiGatewayHost,
        [Description("Source environment name (e.g. 'dev'). Auto-detected from Terraform if omitted.")] string? sourceEnvironment = null,
        [Description("Existing target environment's Terraform HCL for merge/sync. " +
                     "When provided, operations are matched by url_template + method: " +
                     "source operations sync into target, target-only operations are preserved.")] string? existingTargetTerraform = null,
        [Description("Target frontend host for CORS origins (e.g. 'portal')")] string? targetFrontendHost = null,
        [Description("Target company domain for CORS origins (e.g. 'company.com')")] string? targetCompanyDomain = null,
        [Description("Target local dev host for CORS (e.g. 'localhost')")] string? targetLocalDevHost = null,
        [Description("Target local dev port for CORS (e.g. '3000')")] string? targetLocalDevPort = null,
        [Description("Override subscription_required for the target environment")] bool? targetSubscriptionRequired = null)
    {
        var settings = new EnvironmentTransformSettings
        {
            SourceEnvironment = sourceEnvironment,
            TargetEnvironment = targetEnvironment,
            TargetStageGroupName = targetStageGroupName,
            TargetApimName = targetApimName,
            TargetApiGatewayHost = targetApiGatewayHost,
            TargetFrontendHost = targetFrontendHost,
            TargetCompanyDomain = targetCompanyDomain,
            TargetLocalDevHost = targetLocalDevHost,
            TargetLocalDevPort = targetLocalDevPort,
            TargetSubscriptionRequired = targetSubscriptionRequired
        };

        var result = transformer.Transform(sourceTerraform, settings, existingTargetTerraform);

        if (!result.Success)
            return "Transform failed:\n" + string.Join("\n", result.Errors);

        var sb = new StringBuilder();
        sb.Append(result.TransformedTerraform);

        if (result.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("// Warnings:");
            foreach (var w in result.Warnings)
                sb.AppendLine($"// - {w}");
        }

        // Append a human-readable change summary
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"// Environment transform: {result.DetectedSourceEnvironment} -> {targetEnvironment}");

        if (result.Summary != null)
        {
            sb.AppendLine($"// Total operations: {result.Summary.TotalOperations}");

            if (result.Summary.SyncedOperations.Count > 0)
            {
                sb.AppendLine($"// Synced ({result.Summary.SyncedOperations.Count}):");
                foreach (var op in result.Summary.SyncedOperations)
                    sb.AppendLine($"//   {op}");
            }

            if (result.Summary.AddedOperations.Count > 0)
            {
                sb.AppendLine($"// Added ({result.Summary.AddedOperations.Count}):");
                foreach (var op in result.Summary.AddedOperations)
                    sb.AppendLine($"//   {op}");
            }

            if (result.Summary.PreservedOperations.Count > 0)
            {
                sb.AppendLine($"// Preserved (target-only, {result.Summary.PreservedOperations.Count}):");
                foreach (var op in result.Summary.PreservedOperations)
                    sb.AppendLine($"//   {op}");
            }
        }

        return sb.ToString();
    }
}
