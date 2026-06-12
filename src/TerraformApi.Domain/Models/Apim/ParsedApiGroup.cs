using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Models.Apim;

/// <summary>
/// One <c>&lt;api_group_name&gt; = { product = [...], api = [...], api_operations = [...] }</c>
/// block extracted from the HCL.
/// </summary>
public sealed record ParsedApiGroup
{
    /// <summary>The group key exactly as written in HCL (may contain ${...}).</summary>
    public required string ApiGroupName { get; init; }

    /// <summary>True when the group key was quoted in the source.</summary>
    public bool KeyIsQuoted { get; init; }

    /// <summary>The AST node containing api / api_operations / product.</summary>
    public required HclObject AstNode { get; init; }

    public List<ParsedApi> Apis { get; init; } = [];
    public List<ParsedApiOperation> Operations { get; init; } = [];
}
