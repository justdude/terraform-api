namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// An element of an object body (or the document root):
/// either a key/value assignment or a comment we want to preserve.
/// </summary>
public abstract record HclObjectItem : HclNode
{
    /// <summary>
    /// Number of blank lines that preceded this item in the source. Recorded by
    /// the parser and re-emitted by the writer's canonical (slow) path so a
    /// re-rendered document keeps its section spacing. Zero for items created
    /// programmatically.
    /// </summary>
    public int BlankLinesBefore { get; set; }
}
