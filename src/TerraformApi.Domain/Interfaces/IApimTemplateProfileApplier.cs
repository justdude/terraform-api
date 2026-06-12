using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// One-time conversions between literal and templated styles.
/// NOT append-only: Apply may change literal values to interpolations.
/// Used only in the explicit apply_template_profile flow.
/// </summary>
public interface IApimTemplateProfileApplier
{
    /// <summary>
    /// Replaces literal field values with the profile's interpolation templates
    /// (and/or fills missing fields). Returns the change log.
    /// </summary>
    List<string> Apply(
        ParsedApimDocument document,
        ApimTemplateProfile profile,
        ApplyProfileOptions options);

    /// <summary>
    /// Reverse operation: substitutes variable values into placeholders,
    /// producing resolved literal HCL for a specific environment.
    /// Returns the change log; unresolved expressions go to <paramref name="warnings"/>.
    /// </summary>
    List<string> Resolve(
        ParsedApimDocument document,
        IReadOnlyDictionary<string, string> variableValues,
        List<string> warnings);
}
