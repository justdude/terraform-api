using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models;

namespace TerraformMerge.Engine;

/// <summary>Which pane / document an operation came from.</summary>
public enum OperationSource
{
    OriginalTerraform,
    DiffTerraform,
    TargetTerraform,
    TargetOpenApi
}

/// <summary>
/// One API method (an APIM <c>api_operation</c> / OpenAPI operation) treated as
/// a node in the similarity graph. Terraform-sourced nodes keep their original
/// AST array item so a rewrite can reproduce them byte-for-byte; OpenAPI-sourced
/// nodes keep the parsed <see cref="OperationInfo"/> so a block can be generated.
///
/// The scalar fields are mutable so the merge editor can accept a value from
/// either side (see <see cref="OperationEditor"/>). <see cref="OperationId"/> is
/// deliberately read-only — it is the operation's identity, so it is never
/// merged across sides.
/// </summary>
public sealed class OperationNode
{
    public required OperationSource Source { get; init; }

    /// <summary>Operation id as written (may contain ${...} interpolations or {tag} placeholders). Read-only identity.</summary>
    public string OperationId { get; init; } = "";

    /// <summary>HTTP method, upper-cased.</summary>
    public string Method { get; set; } = "";

    /// <summary>URL template as written (e.g. "users/{id}" or "${operation_path}").</summary>
    public string UrlTemplate { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int? StatusCode { get; set; }

    /// <summary>APIM api name the operation belongs to (e.g. "orders-api-dev"). Editable.</summary>
    public string ApiName { get; set; } = "";

    /// <summary>APIM resource group name (e.g. "rg-apim-dev"). Editable.</summary>
    public string ApimResourceGroupName { get; set; } = "";

    /// <summary>APIM instance name (e.g. "apim-company-dev"). Editable.</summary>
    public string ApimName { get; set; } = "";

    /// <summary>Sorted parameter keys, "in:name" lower-cased (e.g. "header:authorization").</summary>
    public IReadOnlyList<string> ParameterKeys { get; init; } = [];

    public IReadOnlyList<int> ResponseCodes { get; init; } = [];

    /// <summary>Terraform-sourced only: the original array item, for faithful re-emit.</summary>
    public HclArrayItem? ArrayItem { get; init; }

    /// <summary>OpenAPI-sourced only: enough to generate an HCL block.</summary>
    public OperationInfo? OpenApiOperation { get; init; }

    /// <summary>Group the operation belongs to (Terraform), used to target the right block on save.</summary>
    public string? ApiGroupName { get; init; }

    /// <summary>
    /// The specific fields changed through the editor. For a generated block the
    /// builder honors the node's own value for a field only when that field was
    /// edited — so editing, say, the description never repoints the operation's
    /// api/resource-group/apim to the source side's values.
    /// </summary>
    public HashSet<OperationField> EditedFields { get; } = [];

    /// <summary>True once any field has been changed through the editor. UI marker only.</summary>
    public bool Edited => EditedFields.Count > 0;

    /// <summary>Label shown in the list boxes.</summary>
    public string Display =>
        ($"{Method,-6} {UrlTemplate}   ({OperationId})".TrimEnd()) + (Edited ? "  •edited" : "");

    /// <summary>Syntactically normalized URL (trimmed, collapsed slashes) for comparison.</summary>
    public string NormalizedUrl => UrlNormalizer.Normalize(UrlTemplate);

    /// <summary>URL with parameter names collapsed to {} — coarse structural key.</summary>
    public string CanonicalUrl => UrlNormalizer.Canonical(UrlTemplate);

    public override string ToString() => Display;
}
