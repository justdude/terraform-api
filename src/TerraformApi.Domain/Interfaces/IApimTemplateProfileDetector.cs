using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Analyzes an existing Terraform file's templating style so new operations
/// can be generated in the same style (literal → literal, template → template).
/// </summary>
public interface IApimTemplateProfileDetector
{
    /// <summary>Returns the detected profile + diagnostics + suggestions.</summary>
    DetectedProfile Detect(ParsedApimDocument document);
}
