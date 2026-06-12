using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Serializes an HCL AST back to text. With
/// <see cref="HclWriteOptions.PreserveOriginalFormatting"/> enabled, unmodified
/// nodes are emitted byte-for-byte from their original source slices; only new
/// or dirty nodes are rendered canonically. This is the "minimal change"
/// guarantee required by append-only sync.
/// </summary>
public interface IHclWriter
{
    string Write(HclDocument document, HclWriteOptions? options = null);
}

/// <summary>Formatting options for <see cref="IHclWriter"/>.</summary>
public sealed record HclWriteOptions
{
    public int IndentSize { get; init; } = 2;

    /// <summary>Align the <c>=</c> of sibling assignments in one column (as in real APIM configs).</summary>
    public bool AlignAssignmentEquals { get; init; } = true;

    /// <summary>Maximum key width used for alignment padding.</summary>
    public int MaxAlignedKeyLength { get; init; } = 36;

    public string LineEnding { get; init; } = "\n";

    /// <summary>
    /// When true (default), unmodified nodes parsed from source are emitted
    /// byte-for-byte from the original text.
    /// </summary>
    public bool PreserveOriginalFormatting { get; init; } = true;
}
