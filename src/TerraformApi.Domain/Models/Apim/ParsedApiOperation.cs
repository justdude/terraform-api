using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Models.Apim;

/// <summary>
/// One element of the <c>api_operations = [ { ... } ]</c> array with
/// extracted field references plus raw AST handles for request/response arrays.
/// </summary>
public sealed record ParsedApiOperation
{
    /// <summary>The operation object in the AST (the modification point for enrichment).</summary>
    public required HclObject AstNode { get; init; }

    /// <summary>The array item wrapper, giving access to leading comments.</summary>
    public HclArrayItem? ArrayItem { get; init; }

    public required HclValueRef OperationId { get; init; }
    public required HclValueRef Method { get; init; }
    public required HclValueRef UrlTemplate { get; init; }
    public HclValueRef? ApimResourceGroupName { get; init; }
    public HclValueRef? ApiName { get; init; }
    public HclValueRef? DisplayName { get; init; }
    public HclValueRef? StatusCode { get; init; }
    public HclValueRef? Description { get; init; }

    /// <summary>
    /// The <c>request = [ ... ]</c> array as a raw AST reference, so merging its
    /// parameters is an operation on AST arrays.
    /// </summary>
    public HclArray? RequestArray { get; init; }

    /// <summary>The <c>response = [ ... ]</c> array as a raw AST reference.</summary>
    public HclArray? ResponsesArray { get; init; }
}
