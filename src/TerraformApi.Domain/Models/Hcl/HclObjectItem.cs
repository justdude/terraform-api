namespace TerraformApi.Domain.Models.Hcl;

/// <summary>
/// An element of an object body (or the document root):
/// either a key/value assignment or a comment we want to preserve.
/// </summary>
public abstract record HclObjectItem : HclNode;
