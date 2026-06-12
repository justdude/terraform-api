namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Behavior switches for one append-only sync run.
/// </summary>
public sealed record SyncOptions
{
    /// <summary>
    /// Force a specific template profile for newly inserted operations.
    /// Null (default) — use the profile auto-detected from the existing file.
    /// </summary>
    public ApimTemplateProfile? OverrideProfile { get; init; }

    /// <summary>Add the 2–3 line leading comment block above inserted operations.</summary>
    public bool AddOperationComments { get; init; } = true;

    /// <summary>Add/maintain the REPLACE BEFORE APPLY header before api_operations.</summary>
    public bool AddReplaceBeforeApplyHeader { get; init; } = true;
}
