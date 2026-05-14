namespace TerraformApi.Api.Dtos;

/// <summary>
/// Response from the cross-environment Terraform transform endpoint.
/// </summary>
public sealed record TransformResponse
{
    public bool Success { get; init; }
    public string TransformedTerraform { get; init; } = "";
    public string? DetectedSourceEnvironment { get; init; }
    public TransformSummaryDto? Summary { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Summary of changes made during the environment transform.
/// </summary>
public sealed class TransformSummaryDto
{
    public int TotalOperations { get; init; }

    /// <summary>Operations present in both source and target, updated from source.</summary>
    public List<string> SyncedOperations { get; init; } = [];

    /// <summary>Operations in source but not in existing target (newly added).</summary>
    public List<string> AddedOperations { get; init; } = [];

    /// <summary>Operations in existing target but not in source (preserved as-is).</summary>
    public List<string> PreservedOperations { get; init; } = [];
}
