namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// A string containing Terraform interpolation (<c>"${api_name}-${env}"</c>)
/// or a bare expression (<c>var.foo</c>). The text is preserved verbatim so
/// values can be compared structurally without resolving variables.
/// </summary>
public sealed record HclInterpolation : HclValue
{
    /// <summary>
    /// The full source text between the quotes, including <c>${...}</c> markers.
    /// Example: <c>"${api_name}-${env}"</c> → InnerText = <c>${api_name}-${env}</c>.
    /// For bare expressions: the expression text itself (e.g. <c>var.foo</c>).
    /// </summary>
    public required string InnerText { get; init; }

    /// <summary>
    /// Expression names referenced inside <c>${...}</c> blocks, in order of appearance.
    /// For <c>"${api_name}-${env}"</c> this is ["api_name", "env"].
    /// For a bare expression: the expression itself.
    /// </summary>
    public IReadOnlyList<string> ReferencedExpressions { get; init; } = [];

    /// <summary>
    /// True when the value was an unquoted expression (<c>protocols = var.protocols</c>)
    /// rather than a quoted interpolated string. Affects how the writer emits it.
    /// </summary>
    public bool Bare { get; init; }
}
