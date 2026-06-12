namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// Root node of a parsed HCL document: a sequence of top-level items
/// (assignments and comments) plus the original source text used for
/// format-preserving round-trips.
/// </summary>
public sealed record HclDocument
{
    /// <summary>Top-level items in document order (assignments and comments).</summary>
    public List<HclObjectItem> RootItems { get; init; } = [];

    /// <summary>Convenience filter returning only the top-level assignments.</summary>
    public IEnumerable<HclAssignment> RootAssignments => RootItems.OfType<HclAssignment>();

    /// <summary>
    /// The raw source the document was parsed from. Used by the writer to emit
    /// unmodified nodes byte-for-byte. Null for documents built from scratch.
    /// </summary>
    public string? OriginalSource { get; init; }
}
