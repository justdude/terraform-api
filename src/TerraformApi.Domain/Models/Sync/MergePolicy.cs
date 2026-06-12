namespace TerraformApi.Domain.Models.Sync;

/// <summary>Per-scalar-field merge behavior.</summary>
public enum FieldMergePolicy
{
    /// <summary>Never touch the existing value.</summary>
    Preserve,

    /// <summary>Write only if the field is missing or empty (null / "" / empty array).</summary>
    EnrichIfMissing,

    /// <summary>Overwrite unconditionally (prohibited for all fields by default in Sync).</summary>
    Overwrite
}

/// <summary>Per-collection merge behavior (request.header[], responses[], ...).</summary>
public enum CollectionMergePolicy
{
    /// <summary>Never change the collection.</summary>
    Preserve,

    /// <summary>Add elements from OpenAPI that are not in Terraform; existing remain untouched.</summary>
    AppendMissing,

    /// <summary>AppendMissing + recursively enrich fields of matched elements.</summary>
    AppendAndEnrich,

    /// <summary>Complete replacement (prohibited in Sync).</summary>
    Replace
}

/// <summary>What to do with an operation present in TF but absent from OpenAPI.</summary>
public enum OperationPreservationMode
{
    /// <summary>Append-only default: leave as is.</summary>
    Preserve,

    /// <summary>Mark deprecated in the description (do not delete!).</summary>
    MarkDeprecated,

    /// <summary>Delete (only for Convert-from-scratch flows).</summary>
    Remove
}

/// <summary>What to do with an operation present in OpenAPI but absent from TF.</summary>
public enum NewOperationMode
{
    /// <summary>Add to the end of the api_operations array.</summary>
    Append,

    /// <summary>Do not add — only report.</summary>
    ReportOnly,

    /// <summary>Add at a special place (e.g. before a comment marker).</summary>
    AppendBeforeMarker
}

/// <summary>
/// Append-only merge policy: per-field for scalars, per-path for collections.
/// The defaults express append-only semantics — nothing existing is ever
/// modified or removed; only missing data is added.
/// </summary>
public sealed record MergePolicy
{
    /// <summary>Operations in TF but not in OpenAPI. Append-only ⇒ Preserve.</summary>
    public OperationPreservationMode UnknownOperationPolicy { get; init; }
        = OperationPreservationMode.Preserve;

    /// <summary>Operations in OpenAPI but not in TF.</summary>
    public NewOperationMode NewOperationPolicy { get; init; }
        = NewOperationMode.Append;

    /// <summary>
    /// Per-field policy for existing operations. Key is the APIM operation field
    /// name (operation_id, display_name, method, url_template, status_code, description).
    /// </summary>
    public IReadOnlyDictionary<string, FieldMergePolicy> OperationFieldPolicies { get; init; }
        = DefaultAppendOnlyFieldPolicies;

    /// <summary>Policy for collections within an operation.</summary>
    public IReadOnlyDictionary<string, CollectionMergePolicy> CollectionPolicies { get; init; }
        = DefaultAppendOnlyCollectionPolicies;

    /// <summary>Per-field policy for the api block.</summary>
    public IReadOnlyDictionary<string, FieldMergePolicy> ApiFieldPolicies { get; init; }
        = DefaultAppendOnlyApiFieldPolicies;

    /// <summary>Returns a copy of this policy with one operation-field policy overridden.</summary>
    public MergePolicy WithOverride(string fieldName, FieldMergePolicy policy)
    {
        var overrides = new Dictionary<string, FieldMergePolicy>(OperationFieldPolicies)
        {
            [fieldName] = policy
        };
        return this with { OperationFieldPolicies = overrides };
    }

    public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
        DefaultAppendOnlyFieldPolicies = new Dictionary<string, FieldMergePolicy>
        {
            ["operation_id"] = FieldMergePolicy.Preserve, // identity, do not touch
            ["method"] = FieldMergePolicy.Preserve,       // do not change the method type
            ["url_template"] = FieldMergePolicy.Preserve, // do not change the URL
            ["display_name"] = FieldMergePolicy.EnrichIfMissing,
            ["description"] = FieldMergePolicy.EnrichIfMissing,
            ["status_code"] = FieldMergePolicy.EnrichIfMissing
        };

    public static readonly IReadOnlyDictionary<string, CollectionMergePolicy>
        DefaultAppendOnlyCollectionPolicies = new Dictionary<string, CollectionMergePolicy>
        {
            ["request.header"] = CollectionMergePolicy.AppendMissing,
            ["request.query"] = CollectionMergePolicy.AppendMissing,
            ["request.template"] = CollectionMergePolicy.AppendMissing,
            ["responses"] = CollectionMergePolicy.AppendMissing,
            ["responses.header"] = CollectionMergePolicy.AppendMissing,
            ["responses.representation"] = CollectionMergePolicy.AppendMissing
        };

    public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
        DefaultAppendOnlyApiFieldPolicies = new Dictionary<string, FieldMergePolicy>
        {
            ["name"] = FieldMergePolicy.Preserve,
            ["display_name"] = FieldMergePolicy.Preserve,
            ["path"] = FieldMergePolicy.Preserve,
            ["service_url"] = FieldMergePolicy.Preserve,
            ["policy"] = FieldMergePolicy.Preserve,
            ["protocols"] = FieldMergePolicy.Preserve,
            ["revision"] = FieldMergePolicy.Preserve
        };
}
