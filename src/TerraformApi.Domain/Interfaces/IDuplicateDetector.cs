using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Finds duplicate operations within an existing Terraform document by running
/// the matching keys over the whole set (structural mode).
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>Returns groups where more than one operation shares the same key value.</summary>
    List<DuplicateGroup> Detect(
        ParsedApimDocument parsed,
        OperationMatchStrategy strategy);
}
