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
/// </summary>
public sealed class OperationNode
{
    public required OperationSource Source { get; init; }

    /// <summary>Operation id as written (may contain ${...} interpolations or {tag} placeholders).</summary>
    public string OperationId { get; init; } = "";

    /// <summary>HTTP method, upper-cased.</summary>
    public string Method { get; init; } = "";

    /// <summary>URL template as written (e.g. "users/{id}" or "${operation_path}").</summary>
    public string UrlTemplate { get; init; } = "";

    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public int? StatusCode { get; init; }

    /// <summary>Sorted parameter keys, "in:name" lower-cased (e.g. "header:authorization").</summary>
    public IReadOnlyList<string> ParameterKeys { get; init; } = [];

    public IReadOnlyList<int> ResponseCodes { get; init; } = [];

    /// <summary>Terraform-sourced only: the original array item, for faithful re-emit.</summary>
    public HclArrayItem? ArrayItem { get; init; }

    /// <summary>OpenAPI-sourced only: enough to generate an HCL block.</summary>
    public OperationInfo? OpenApiOperation { get; init; }

    /// <summary>Group the operation belongs to (Terraform), used to target the right block on save.</summary>
    public string? ApiGroupName { get; init; }

    /// <summary>Label shown in the list boxes.</summary>
    public string Display => $"{Method,-6} {UrlTemplate}   ({OperationId})".TrimEnd();

    /// <summary>Syntactically normalized URL (trimmed, collapsed slashes) for comparison.</summary>
    public string NormalizedUrl => UrlNormalizer.Normalize(UrlTemplate);

    /// <summary>URL with parameter names collapsed to {} — coarse structural key.</summary>
    public string CanonicalUrl => UrlNormalizer.Canonical(UrlTemplate);

    public override string ToString() => Display;
}
