namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Input for an append-only sync run: the OpenAPI spec, the existing Terraform
/// (may be empty for from-scratch generation), conversion settings, and
/// optional policy / strategy / option overrides.
/// </summary>
public sealed record SyncRequest
{
    public required string OpenApiJson { get; init; }

    /// <summary>Existing Terraform HCL. Null/empty → generate from scratch.</summary>
    public string? ExistingTerraform { get; init; }

    public required ConversionSettings Settings { get; init; }

    /// <summary>Merge policy. Null → append-only defaults.</summary>
    public MergePolicy? MergePolicy { get; init; }

    /// <summary>Match strategy. Null → [MethodAndUrl, OperationId, Tag].</summary>
    public OperationMatchStrategy? MatchStrategy { get; init; }

    /// <summary>Sync behavior options (profile override, comments, header).</summary>
    public SyncOptions? Options { get; init; }
}
