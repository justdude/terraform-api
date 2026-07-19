using System.Text.RegularExpressions;

namespace TerraformMerge.Engine;

/// <summary>
/// Computes a similarity score in [0,1] between two <see cref="OperationNode"/>s
/// as a weighted blend of method, URL, operation-id and parameter-set
/// similarity. The block aligner uses these scores as graph-edge weights.
/// </summary>
public static partial class SimilarityScorer
{
    // Weights sum to 1. Method + URL dominate because they define the route;
    // operation_id differs across environments (${...}-${env}) so it is a weak
    // signal, and parameters are a tie-breaker.
    private const double MethodWeight = 0.35;
    private const double UrlWeight = 0.40;
    private const double OperationIdWeight = 0.15;
    private const double ParameterWeight = 0.10;

    /// <summary>
    /// Minimum route similarity for a pair to be eligible at all. Without it,
    /// two operations with no ids and no parameters (where only method + URL
    /// carry evidence, and a shared method alone is worth 0.35/0.75 = 0.47 after
    /// renormalization) drift over the match threshold — GET /users vs
    /// GET /orders scored 0.60. The floor separates unrelated routes (~0.17)
    /// from genuinely related ones such as v1/users vs v2/users (~0.60).
    /// </summary>
    internal const double MinimumUrlSimilarity = 0.34;

    /// <summary>
    /// Ceiling applied when the pair fails an identity gate. An APIM operation
    /// is identified by <c>(method, url_template)</c>: a different method or a
    /// too-different route means a <b>different operation</b>, and pairing them
    /// during a merge would hide a genuinely new operation instead of offering
    /// it as an addition. Capping (rather than zeroing) keeps scores ordered for
    /// diagnostics; it sits below <see cref="BlockAligner.DefaultThreshold"/>, so
    /// a caller that deliberately lowers the threshold under this ceiling can
    /// still explore fuzzy cross-identity pairings.
    /// </summary>
    internal const double IdentityMismatchCeiling = 0.50;

    [GeneratedRegex(@"\$\{[^}]*\}")]
    private static partial Regex Interpolation();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex PlaceholderTag();

    /// <summary>
    /// 1 = identical, 0 = nothing in common. Components with no evidence on
    /// either side (both operation ids empty, or neither has parameters) are
    /// excluded and the remaining weights renormalized — otherwise two unrelated
    /// routes would score highly just for sharing an (empty) id and (empty)
    /// parameter set. Method and URL always contribute.
    /// </summary>
    public static double Similarity(OperationNode a, OperationNode b)
    {
        var weightedSum = 0.0;
        var totalWeight = 0.0;

        void Add(double weight, double value)
        {
            weightedSum += weight * value;
            totalWeight += weight;
        }

        var url = UrlSimilarity(a, b);
        var methodMatches = string.Equals(a.Method, b.Method, StringComparison.OrdinalIgnoreCase);

        Add(MethodWeight, methodMatches ? 1.0 : 0.0);
        Add(UrlWeight, url);

        if (!(string.IsNullOrWhiteSpace(a.OperationId) && string.IsNullOrWhiteSpace(b.OperationId)))
            Add(OperationIdWeight, OperationIdSimilarity(a.OperationId, b.OperationId));

        if (a.ParameterKeys.Count > 0 || b.ParameterKeys.Count > 0)
            Add(ParameterWeight, TextDistance.Jaccard(a.ParameterKeys, b.ParameterKeys));

        var score = totalWeight == 0.0 ? 0.0 : weightedSum / totalWeight;

        // Identity gates — (method, url_template) identifies an APIM operation.
        if (url < MinimumUrlSimilarity)
            return Math.Min(score, url);
        if (!methodMatches)
            return Math.Min(score, IdentityMismatchCeiling);

        return score;
    }

    /// <summary>Distance = 1 - similarity.</summary>
    public static double Distance(OperationNode a, OperationNode b) => 1.0 - Similarity(a, b);

    /// <summary>
    /// URL similarity: average of segment-set Jaccard and (1 - normalized edit
    /// distance) on the canonical form (parameter names collapsed). Falls back
    /// to the name-preserving form when both canonical URLs are identical, so
    /// two routes differing only by a param name still separate from unrelated
    /// routes when the exact form matters.
    /// </summary>
    internal static double UrlSimilarity(OperationNode a, OperationNode b)
    {
        var canonicalA = a.CanonicalUrl;
        var canonicalB = b.CanonicalUrl;

        var jaccard = TextDistance.Jaccard(
            UrlNormalizer.Segments(canonicalA), UrlNormalizer.Segments(canonicalB));
        var edit = 1.0 - TextDistance.NormalizedLevenshtein(canonicalA, canonicalB);
        var canonicalScore = 0.5 * jaccard + 0.5 * edit;

        // When canonical forms tie, refine with the name-preserving comparison
        // so /users/{id} and /users/{userId} still score slightly below exact.
        if (canonicalScore >= 0.999)
        {
            var nameEdit = 1.0 - TextDistance.NormalizedLevenshtein(a.NormalizedUrl, b.NormalizedUrl);
            return 0.9 + 0.1 * nameEdit;
        }

        return canonicalScore;
    }

    /// <summary>
    /// Operation-id similarity, tolerant of interpolations and placeholder tags
    /// (which vary by environment): they are stripped to a common marker before
    /// the edit-distance comparison so "${op}-${env}" ≈ "${op}-${env}".
    /// </summary>
    internal static double OperationIdSimilarity(string a, string b)
    {
        var normA = NormalizeId(a);
        var normB = NormalizeId(b);

        if (normA.Length == 0 && normB.Length == 0)
            return 1.0;

        return 1.0 - TextDistance.NormalizedLevenshtein(normA, normB);
    }

    private static string NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";
        var value = Interpolation().Replace(id, "*");
        value = PlaceholderTag().Replace(value, "*");
        return value.Trim().ToLowerInvariant();
    }
}
