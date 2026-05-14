using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Transforms a Terraform APIM configuration from one environment to another.
/// Supports auto-detecting the source environment and optionally merging with
/// an existing target environment's configuration. Operations are matched
/// across environments by url_template + HTTP method, not by operation_id.
/// </summary>
public interface IEnvironmentTransformer
{
    /// <summary>
    /// Transforms the source Terraform to the target environment specified in settings.
    /// If <paramref name="existingTargetTerraform"/> is provided, merges the result:
    /// source operations are synced/added, target-only operations are preserved.
    /// </summary>
    /// <param name="sourceTerraform">The source environment's Terraform HCL (e.g. the dev config).</param>
    /// <param name="settings">Target environment settings (resource group, APIM name, gateway host, etc.).</param>
    /// <param name="existingTargetTerraform">Optional existing target environment's Terraform HCL for merge/sync.</param>
    EnvironmentTransformResult Transform(
        string sourceTerraform,
        EnvironmentTransformSettings settings,
        string? existingTargetTerraform = null);
}
