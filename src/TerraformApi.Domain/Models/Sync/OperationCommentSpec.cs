namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Input for building the leading comment block placed above an inserted operation.
/// Format (§REV-1.5.4):
/// line 1: "METHOD URL_TEMPLATE | op_id: ID" (strictly only this),
/// line 2: display_name / source / inserted date,
/// line 3 (only when placeholders exist): "placeholders to replace: ...".
/// </summary>
public sealed record OperationCommentSpec
{
    public required string Method { get; init; }
    public required string UrlTemplate { get; init; }
    public required string OperationId { get; init; }
    public string? DisplayName { get; init; }
    public required OperationCommentSource Source { get; init; }
    public DateTime InsertedAt { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<string> PlaceholdersToReplace { get; init; } = [];
}

/// <summary>Where the inserted operation came from.</summary>
public enum OperationCommentSource
{
    OpenApi,
    Generated,
    ManuallyAdded,
    PreservedFromExisting
}
