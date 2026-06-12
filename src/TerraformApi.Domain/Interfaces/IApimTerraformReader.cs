using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Extracts the APIM structure (api groups, api blocks, operations) from an HCL
/// document, keeping references into the AST so modifications round-trip.
/// The reader knows nothing about OpenAPI; it only navigates the AST.
/// </summary>
public interface IApimTerraformReader
{
    ParsedApimDocument Read(string terraformSource);

    ParsedApimDocument Read(HclDocument document);

    /// <summary>
    /// Structural patterns the reader understands, tried in order. Each is the
    /// path from the root to the parent of the api group blocks; an empty path
    /// means the flat layout (<c>api_group = { ... }</c> at the root).
    /// </summary>
    IReadOnlyList<IReadOnlyList<string>> KnownApiGroupPaths { get; }
}
