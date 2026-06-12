using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Writes a parsed APIM document back to HCL text and builds fresh documents
/// from an <see cref="ApimConfiguration"/> using a template profile.
/// </summary>
public interface IApimTerraformWriter
{
    /// <summary>Writes after AST modifications. Inherits options from the HCL writer.</summary>
    string Write(ParsedApimDocument parsed, HclWriteOptions? options = null);

    /// <summary>
    /// Constructs a <see cref="ParsedApimDocument"/> from scratch using the
    /// template profile in <paramref name="options"/>. Each operation receives
    /// leading comments when enabled.
    /// </summary>
    ParsedApimDocument BuildFromConfiguration(
        ApimConfiguration configuration,
        BuildOptions options);
}
