using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// High-level entry points for the sync engine: append-only sync,
/// read-only analysis, and template profile application.
/// </summary>
public interface ISyncOrchestrator
{
    /// <summary>
    /// Append-only sync of existing Terraform with an OpenAPI spec.
    /// Empty existing Terraform → generate from scratch using the profile.
    /// </summary>
    SyncResult Sync(SyncRequest request);

    /// <summary>Analysis of an existing file without modifications.</summary>
    AnalyzeResult Analyze(string existingTerraform);

    /// <summary>Applying (templatize) or removing (resolve) a template profile.</summary>
    ApplyProfileResult ApplyProfile(
        string existingTerraform,
        ApimTemplateProfile? profile,
        ApplyProfileOptions options,
        IReadOnlyDictionary<string, string>? variableValues = null,
        bool resolve = false);
}
