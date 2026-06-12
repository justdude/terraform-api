namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Composite "fingerprint" of an operation used for matching between
/// OpenAPI and Terraform. No field is required — the filled ones are used
/// for comparison according to <see cref="OperationMatchStrategy.Keys"/>.
/// </summary>
public sealed record OperationFingerprint
{
    public string? OperationId { get; init; }

    /// <summary>HTTP method, normalized to UPPER.</summary>
    public string? Method { get; init; }

    /// <summary>URL template, normalized per <see cref="UrlNormalizationOptions"/>.</summary>
    public string? UrlTemplate { get; init; }

    /// <summary>Sorted parameter signature, e.g. "h:Authorization|q:limit".</summary>
    public string? ParameterSignature { get; init; }

    public string? Tag { get; init; }

    /// <summary>For disambiguation between APIs in the same group.</summary>
    public string? ApiName { get; init; }

    /// <summary>APIM resource group (for the strictest scope key).</summary>
    public string? ApiResourceGroup { get; init; }

    /// <summary>What the fingerprint was built from (for debugging/reporting).</summary>
    public OperationFingerprintSource SourceMarker { get; init; }

    /// <summary>Index of the source operation in its original list (back-reference).</summary>
    public int SourceIndex { get; init; } = -1;
}

/// <summary>The origin of a fingerprint.</summary>
public enum OperationFingerprintSource
{
    OpenApi,
    ExistingTerraform,

    /// <summary>After applying the interpolation resolver.</summary>
    Resolved
}
