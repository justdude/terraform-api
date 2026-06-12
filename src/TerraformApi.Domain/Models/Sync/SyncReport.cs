namespace TerraformApi.Domain.Models.Sync;

/// <summary>Diff for one operation pair (or unpaired operation).</summary>
public sealed record OperationDiff
{
    public OperationFingerprint? TerraformFingerprint { get; init; }
    public OperationFingerprint? OpenApiFingerprint { get; init; }
    public required OperationDiffKind Kind { get; init; }
    public List<FieldDiff> FieldDiffs { get; init; } = [];

    /// <summary>What was actually applied (after passing through MergePolicy).</summary>
    public List<string> AppliedChanges { get; init; } = [];

    public List<string> SkippedDueToPolicy { get; init; } = [];
}

/// <summary>Classification of an operation diff.</summary>
public enum OperationDiffKind
{
    /// <summary>Present in both, no difference.</summary>
    Identical,

    /// <summary>Present in both with differences (see FieldDiffs).</summary>
    Changed,

    /// <summary>Only in OpenAPI → will be added.</summary>
    AddedFromOpenApi,

    /// <summary>Only in Terraform → preserved as is (append-only).</summary>
    PreservedFromTerraform,

    /// <summary>Marked as duplicate (see DuplicateGroups).</summary>
    Duplicate
}

/// <summary>Diff for a single field of an operation.</summary>
public sealed record FieldDiff
{
    /// <summary>"operation_id", "request.header[name=Auth]", etc.</summary>
    public required string FieldPath { get; init; }

    public required string? TerraformValue { get; init; }
    public required string? OpenApiValue { get; init; }
    public required FieldDiffOutcome Outcome { get; init; }
}

/// <summary>What happened to a field during sync.</summary>
public enum FieldDiffOutcome
{
    NoChange,
    AppliedEnrichIfMissing,
    AppliedOverwrite,
    SkippedPreserve,
    AppliedCollectionAppend
}

/// <summary>A group of operations sharing the same matching key value.</summary>
public sealed record DuplicateGroup
{
    public required OperationMatchKey MatchedBy { get; init; }

    /// <summary>e.g. "GET|/users"</summary>
    public required string MatchedValue { get; init; }

    public List<DuplicateMember> Members { get; init; } = [];
}

/// <summary>One member of a duplicate group.</summary>
public sealed record DuplicateMember
{
    /// <summary>As in HCL, possibly with ${...}.</summary>
    public required string OperationId { get; init; }

    public required string ApiGroupName { get; init; }
    public required string ApiName { get; init; }
    public required int LineInSource { get; init; }
    public DuplicateSeverity Severity { get; init; }
}

/// <summary>How serious a duplicate is.</summary>
public enum DuplicateSeverity
{
    /// <summary>Same operation_id within one api_group/api — critical.</summary>
    HardDuplicate,

    /// <summary>Different operation_id but same (method, url) in one API — APIM will reject.</summary>
    LogicalDuplicate,

    /// <summary>Same (method, url) in different APIs — acceptable but suspicious.</summary>
    CrossApiSimilarity
}

/// <summary>Full report of one sync run.</summary>
public sealed record SyncReport
{
    public required DateTime GeneratedAt { get; init; }
    public required string ApiGroupName { get; init; }

    public int TotalOperationsInTerraform { get; init; }
    public int TotalOperationsInOpenApi { get; init; }

    public int OperationsAdded { get; init; }
    public int OperationsPreserved { get; init; }
    public int OperationsEnriched { get; init; }
    public int OperationsIdentical { get; init; }

    public List<OperationDiff> Diffs { get; init; } = [];
    public List<DuplicateGroup> Duplicates { get; init; } = [];

    /// <summary>Warnings that do not block sync but require review.</summary>
    public List<SyncWarning> Warnings { get; init; } = [];
}

/// <summary>A non-blocking sync warning.</summary>
public sealed record SyncWarning
{
    public required string Message { get; init; }
    public string? OperationId { get; init; }
    public SyncWarningKind Kind { get; init; }
}

/// <summary>Warning categories.</summary>
public enum SyncWarningKind
{
    /// <summary>operation_id = "${...}-${env}" — ok, but matching is structural.</summary>
    OperationIdContainsInterpolation,
    UrlTemplateContainsInterpolation,

    /// <summary>Multiple candidates found for a single fingerprint.</summary>
    AmbiguousMatch,
    SkippedFieldDueToPolicy,
    UnknownFieldInOpenApi,
    DuplicateDetected
}

/// <summary>The result of an append-only sync run.</summary>
public sealed record SyncResult
{
    public required bool Success { get; init; }

    /// <summary>The final HCL.</summary>
    public required string TerraformConfig { get; init; }

    public required SyncReport Report { get; init; }
    public List<string> Errors { get; init; } = [];
}
