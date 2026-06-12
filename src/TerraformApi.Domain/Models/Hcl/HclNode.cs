namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// Base type for every node in the HCL AST.
/// Stores the 1-based source position (for error messages) and the
/// character span in the original source (for format-preserving writes).
/// </summary>
public abstract record HclNode
{
    /// <summary>1-based line of the node's first token in the source.</summary>
    public int Line { get; init; }

    /// <summary>1-based column of the node's first token in the source.</summary>
    public int Column { get; init; }

    /// <summary>
    /// Inclusive character offset of the node's first token in the original source,
    /// or -1 when the node was created programmatically (not parsed).
    /// </summary>
    public int StartOffset { get; init; } = -1;

    /// <summary>
    /// Exclusive character offset just past the node's last token,
    /// or -1 when the node was created programmatically.
    /// </summary>
    public int EndOffset { get; init; } = -1;

    /// <summary>
    /// Set to true by mutators (e.g. the synchronizer) when the node has been
    /// modified after parsing. Dirty nodes are re-rendered canonically by the
    /// writer instead of using the original source slice.
    /// </summary>
    public bool Dirty { get; set; }

    /// <summary>True when the node has a valid source span for format-preserving output.</summary>
    public bool HasSourceSpan => StartOffset >= 0 && EndOffset > StartOffset;
}

/// <summary>
/// Base type for any HCL value (object, array, literal, interpolation, heredoc).
/// </summary>
public abstract record HclValue : HclNode;
