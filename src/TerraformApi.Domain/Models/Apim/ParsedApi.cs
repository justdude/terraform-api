using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Models.Apim;

/// <summary>
/// One element of the <c>api = [ { ... } ]</c> array with extracted field references.
/// </summary>
public sealed record ParsedApi
{
    /// <summary>The api object in the AST (the modification point for enrichment).</summary>
    public required HclObject AstNode { get; init; }

    public HclValueRef Name { get; init; } = new();
    public HclValueRef DisplayName { get; init; } = new();
    public HclValueRef ApimResourceGroupName { get; init; } = new();
    public HclValueRef ApimName { get; init; } = new();
    public HclValueRef Path { get; init; } = new();
    public HclValueRef ServiceUrl { get; init; } = new();
    public HclValueRef Revision { get; init; } = new();

    /// <summary>The policy heredoc, when present.</summary>
    public HclValueRef? Policy { get; init; }
}
