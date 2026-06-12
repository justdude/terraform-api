using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Builds the leading comment block placed above inserted operations
/// (format §REV-1.5.4) and extracts placeholders from operation nodes.
/// </summary>
public interface IOperationCommentBuilder
{
    /// <summary>Builds the 2–3 line comment block for one operation.</summary>
    List<HclComment> Build(OperationCommentSpec spec);

    /// <summary>
    /// Scans an operation node and returns all unique ${name} placeholders
    /// referenced in its fields, sorted alphabetically.
    /// </summary>
    IReadOnlyList<string> ExtractPlaceholders(HclObject operationNode);
}
