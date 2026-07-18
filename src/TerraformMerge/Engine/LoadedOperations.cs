using TerraformApi.Domain.Models.Apim;

namespace TerraformMerge.Engine;

/// <summary>
/// The result of loading one source (a pane): the operation nodes plus, for a
/// Terraform source, the parsed document that owns their AST (kept so a rewrite
/// reuses the original blocks byte-for-byte).
/// </summary>
public sealed class LoadedOperations
{
    public required IReadOnlyList<OperationNode> Nodes { get; init; }

    /// <summary>The parsed Terraform document; null for an OpenAPI source.</summary>
    public ParsedApimDocument? TerraformDocument { get; init; }

    /// <summary>Raw source text as loaded.</summary>
    public string SourceText { get; init; } = "";

    public bool IsTerraform => TerraformDocument is not null;

    public static LoadedOperations Empty { get; } = new() { Nodes = [] };
}
