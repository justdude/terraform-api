namespace TerraformApi.Domain.Models;

/// <summary>
/// Result of transforming a Terraform configuration from one APIM environment to another,
/// optionally merged with an existing target environment's configuration.
/// </summary>
public sealed class EnvironmentTransformResult
{
    public bool Success { get; init; }
    public string TransformedTerraform { get; init; } = "";
    public string? DetectedSourceEnvironment { get; init; }
    public TransformSummary? Summary { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Summary of operations affected by the environment transform.
/// Operations are matched by url_template + HTTP method (not by operation_id,
/// since IDs typically differ between environments).
/// </summary>
public sealed class TransformSummary
{
    /// <summary>Total operations in the output terraform.</summary>
    public int TotalOperations { get; init; }

    /// <summary>Operations present in both source and existing target, updated from source. Format: "GET /path".</summary>
    public List<string> SyncedOperations { get; init; } = [];

    /// <summary>Operations in source but not in existing target (newly added). Format: "GET /path".</summary>
    public List<string> AddedOperations { get; init; } = [];

    /// <summary>Operations in existing target but not in source (preserved as-is). Format: "GET /path".</summary>
    public List<string> PreservedOperations { get; init; } = [];
}
