using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Models.Apim;

/// <summary>
/// The APIM-specific view extracted from an HCL document on top of the AST.
/// Holds references into the original AST so that modifications made by the
/// synchronizer are reflected when the AST is written back.
/// </summary>
public sealed record ParsedApimDocument
{
    /// <summary>The original AST that <see cref="ParsedApiGroup"/>s reference through their nodes.</summary>
    public required HclDocument Ast { get; init; }

    /// <summary>
    /// Path from the root to the parent of the <c>api_group_name</c> blocks
    /// (e.g. ["apis", "bpc_apis", "backend_apis"]).
    /// Null when the structure is flat (<c>api_group_name = { ... }</c> at the root).
    /// </summary>
    public IReadOnlyList<string>? ApiGroupParentPath { get; init; }

    /// <summary>All recognized API groups in document order.</summary>
    public List<ParsedApiGroup> ApiGroups { get; init; } = [];

    /// <summary>
    /// Grouping of api blocks and their operations by (resource group, api name).
    /// Key lookup for sync: new operations from OpenAPI go into the correct group.
    /// </summary>
    public IReadOnlyDictionary<ApimApiGroupKey, ApiGroupBundle> ApisByGroupKey { get; init; }
        = new Dictionary<ApimApiGroupKey, ApiGroupBundle>();
}

/// <summary>An api block and the operations that belong to it, keyed by (rg, api_name).</summary>
public sealed record ApiGroupBundle
{
    public required ApimApiGroupKey Key { get; init; }

    /// <summary>The api block, when one with a matching key exists.</summary>
    public ParsedApi? Api { get; set; }

    /// <summary>The api group (HCL container) the bundle's operations live in.</summary>
    public ParsedApiGroup? OwnerGroup { get; set; }

    /// <summary>Operations whose <c>apim_resource_group_name</c> + <c>api_name</c> match the key.</summary>
    public List<ParsedApiOperation> Operations { get; init; } = [];
}
