namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Matching strategy: an ordered list of keys. Comparison goes from top to
/// bottom; the first key that yields exactly one candidate produces the match.
/// </summary>
public sealed record OperationMatchStrategy
{
    /// <summary>Matching order. The default is the safest scheme for cross-environment merging.</summary>
    public IReadOnlyList<OperationMatchKey> Keys { get; init; } =
    [
        OperationMatchKey.MethodAndUrl,
        OperationMatchKey.OperationId,
        OperationMatchKey.Tag
    ];

    /// <summary>URL normalization applied before comparison.</summary>
    public UrlNormalizationOptions UrlNormalization { get; init; } = new();

    /// <summary>Custom matcher used by <see cref="OperationMatchKey.Custom"/>.</summary>
    public Func<OperationFingerprint, OperationFingerprint, bool>? CustomMatcher { get; init; }

    /// <summary>
    /// When enabled and structural-mode comparison yields no match, apply the
    /// interpolation resolver (with <see cref="VariableContext"/>) and retry.
    /// </summary>
    public bool TryResolvedComparisonAsFallback { get; init; } = true;

    /// <summary>Variable values for resolved-mode comparison (e.g. env=dev).</summary>
    public IReadOnlyDictionary<string, string>? VariableContext { get; init; }

    /// <summary>Include parameter types (string/int) in the parameter signature.</summary>
    public bool IncludeParameterTypesInSignature { get; init; }
}

/// <summary>URL normalization rules applied before fingerprint comparison.</summary>
public sealed record UrlNormalizationOptions
{
    /// <summary>HTTPS://... → https://...</summary>
    public bool LowercaseScheme { get; init; } = true;

    /// <summary>/users/ → /users</summary>
    public bool TrimTrailingSlash { get; init; } = true;

    /// <summary>/users//{id} → /users/{id}</summary>
    public bool CollapseSlashes { get; init; } = true;

    /// <summary>
    /// Unify parameter syntax {x} vs :x for matching only (not for writing).
    /// Does NOT normalize parameter names by default.
    /// </summary>
    public bool NormalizeBraceParams { get; init; } = true;

    /// <summary>Treat "users" and "/users" as the same.</summary>
    public bool TreatLeadingSlashAsOptional { get; init; } = true;

    /// <summary>Compare paths case-insensitively (off by default — case in path matters).</summary>
    public bool IgnoreCase { get; init; }
}
