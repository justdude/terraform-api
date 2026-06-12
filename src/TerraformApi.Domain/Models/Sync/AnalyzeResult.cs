namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Result of analyzing an existing Terraform file without modifying it:
/// API groups, operation counts, detected templating profile, and duplicates.
/// </summary>
public sealed record AnalyzeResult
{
    public required bool Success { get; init; }
    public DetectedProfile? DetectedProfile { get; init; }
    public List<ApiGroupSummary> ApiGroups { get; init; } = [];
    public int TotalOperations { get; init; }
    public List<DuplicateGroup> Duplicates { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>One (resource group, api name) pair with its operation count.</summary>
public sealed record ApiGroupSummary
{
    public required string ApimResourceGroupName { get; init; }
    public required string ApiName { get; init; }
    public int OperationCount { get; init; }
}
