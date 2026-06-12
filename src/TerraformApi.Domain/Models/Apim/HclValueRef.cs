using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Models.Apim;

/// <summary>
/// A reference to a value in the AST plus its best text representation
/// for structural comparison.
/// </summary>
public sealed record HclValueRef
{
    /// <summary>The raw AST node, or null when the field is absent.</summary>
    public HclValue? Node { get; init; }

    /// <summary>
    /// Text for structural comparison: literal → RawValue;
    /// interpolation → the entire ${...} text; heredoc → content.
    /// </summary>
    public string? StructuralText =>
        Node switch
        {
            HclLiteral l => l.RawValue,
            HclInterpolation i => i.InnerText,
            HclHeredoc h => h.Content,
            _ => null
        };

    /// <summary>True when the value is an interpolated expression.</summary>
    public bool IsInterpolated => Node is HclInterpolation;
}
