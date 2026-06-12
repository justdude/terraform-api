namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Result of applying a template profile (templatize) or resolving
/// placeholders (resolve) on an existing Terraform document.
/// </summary>
public sealed record ApplyProfileResult
{
    public required bool Success { get; init; }
    public string TerraformConfig { get; init; } = "";

    /// <summary>Human-readable change log, e.g. "api.apim_name: \"x\" → ${apim_name}".</summary>
    public List<string> AppliedChanges { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}
