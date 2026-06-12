using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Builds operation fingerprints from both sides (OpenAPI and Terraform) and
/// matches them using an ordered key strategy with optional resolved-mode fallback.
/// </summary>
public interface IOperationMatcher
{
    /// <summary>Creates a fingerprint from an <see cref="ApimApiOperation"/> (OpenAPI side).</summary>
    OperationFingerprint FingerprintFromOpenApi(
        ApimApiOperation operation,
        OperationMatchStrategy strategy);

    /// <summary>Creates a fingerprint from a <see cref="ParsedApiOperation"/> (Terraform side).</summary>
    OperationFingerprint FingerprintFromTerraform(
        ParsedApiOperation operation,
        OperationMatchStrategy strategy);

    /// <summary>
    /// Matches the two sets. Returns three partitions plus ambiguities.
    /// When <paramref name="scopeKey"/> is given, Terraform fingerprints outside
    /// that (resource group, api name) scope are excluded from matching.
    /// </summary>
    MatchResult Match(
        IReadOnlyList<OperationFingerprint> openApiFingerprints,
        IReadOnlyList<OperationFingerprint> terraformFingerprints,
        OperationMatchStrategy strategy,
        ApimApiGroupKey? scopeKey = null);
}

/// <summary>Result of matching OpenAPI operations against Terraform operations.</summary>
public sealed record MatchResult
{
    /// <summary>Operations from OpenAPI with no corresponding Terraform operation.</summary>
    public List<OperationFingerprint> OnlyInOpenApi { get; init; } = [];

    /// <summary>Terraform operations with no corresponding OpenAPI pair.</summary>
    public List<OperationFingerprint> OnlyInTerraform { get; init; } = [];

    /// <summary>Matched pairs: left side is Terraform, right side is OpenAPI.</summary>
    public List<(OperationFingerprint Tf, OperationFingerprint OpenApi)> Matched { get; init; } = [];

    /// <summary>Cases where one fingerprint matched several candidates.</summary>
    public List<AmbiguousMatch> Ambiguities { get; init; } = [];
}

/// <summary>One fingerprint matching multiple candidates on a key.</summary>
public sealed record AmbiguousMatch
{
    public required OperationFingerprint Source { get; init; }
    public required IReadOnlyList<OperationFingerprint> Candidates { get; init; }
    public required OperationMatchKey AmbiguousOnKey { get; init; }
}
