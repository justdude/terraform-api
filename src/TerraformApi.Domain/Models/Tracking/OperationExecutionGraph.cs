namespace TerraformApi.Domain.Models.Tracking;

/// <summary>
/// A flat graph of all operations after a sync run, with per-operation status
/// and aggregate statistics. Built from a <see cref="Sync.SyncReport"/> —
/// the graph is a consumer of sync results, never a source.
/// </summary>
public sealed record OperationExecutionGraph
{
    public required string ApiGroupName { get; init; }
    public required DateTime GeneratedAt { get; init; }
    public List<OperationNode> Nodes { get; init; } = [];
    public required GraphStatistics Statistics { get; init; }
}

/// <summary>One operation in the execution graph.</summary>
public sealed record OperationNode
{
    /// <summary>Operation id as in HCL (may contain ${...}) or from OpenAPI.</summary>
    public required string OperationId { get; init; }

    public string? Method { get; init; }
    public string? UrlTemplate { get; init; }
    public required OperationNodeStatus Status { get; init; }

    /// <summary>Where the operation came from in this sync run.</summary>
    public required OperationNodeOrigin Origin { get; init; }

    /// <summary>Fields that were actually changed (for Modified nodes).</summary>
    public List<string> AppliedChanges { get; init; } = [];
}

/// <summary>Operation lifecycle status in the resulting configuration.</summary>
public enum OperationNodeStatus
{
    /// <summary>Present and unchanged in the final config.</summary>
    Included,

    /// <summary>Present with fields enriched during this sync.</summary>
    Modified,

    /// <summary>Newly added from OpenAPI during this sync.</summary>
    New,

    /// <summary>Reported but not applied (e.g. ReportOnly policy or ambiguity).</summary>
    Skipped
}

/// <summary>Which side the operation originated from.</summary>
public enum OperationNodeOrigin
{
    OpenApi,
    ExistingTerraform,
    Both
}

/// <summary>Aggregate counters over the graph.</summary>
public sealed record GraphStatistics
{
    /// <summary>All operations present in the final configuration.</summary>
    public int TotalOperations { get; init; }

    /// <summary>Operations newly added from OpenAPI.</summary>
    public int NewOperations { get; init; }

    /// <summary>Operations included in the final config (new + matched + preserved).</summary>
    public int IncludedOperations { get; init; }

    /// <summary>Matched operations that received field enrichment.</summary>
    public int ModifiedOperations { get; init; }

    /// <summary>Terraform-only operations preserved untouched.</summary>
    public int PreservedOperations { get; init; }

    /// <summary>Operations reported but not applied.</summary>
    public int SkippedOperations { get; init; }
}
