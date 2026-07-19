using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class SimilarityScorerTests
{
    [Fact]
    public void Similarity_IdenticalOperations_IsOne()
    {
        var a = TestOps.Op("GET", "users/{id}", "get-user");
        var b = TestOps.Op("GET", "users/{id}", "get-user");
        Assert.Equal(1.0, SimilarityScorer.Similarity(a, b), 6);
    }

    [Fact]
    public void Similarity_MethodMismatch_CappedBelowMethodWeight()
    {
        // Same URL and id, different method → cannot reach the 0.35 method weight.
        var a = TestOps.Op("GET", "users", "op");
        var b = TestOps.Op("POST", "users", "op");
        Assert.True(SimilarityScorer.Similarity(a, b) <= 0.65 + 1e-9);
    }

    [Fact]
    public void Similarity_SameRouteDifferentParamName_HighButBelowExact()
    {
        var exact = SimilarityScorer.Similarity(
            TestOps.Op("GET", "users/{id}"), TestOps.Op("GET", "users/{id}"));
        var renamed = SimilarityScorer.Similarity(
            TestOps.Op("GET", "users/{id}"), TestOps.Op("GET", "users/{userId}"));

        Assert.True(renamed > 0.85, $"expected renamed route to stay similar, got {renamed}");
        Assert.True(renamed < exact, "a renamed parameter should score below an exact match");
    }

    [Fact]
    public void Similarity_SameMethodUnrelatedShortRoutes_StayBelowThreshold()
    {
        // No ids and no params → only method + URL carry evidence. A shared
        // method must never be enough on its own to pair two different routes.
        var a = TestOps.Op("GET", "users");
        var b = TestOps.Op("GET", "orders");

        var score = SimilarityScorer.Similarity(a, b);
        Assert.True(score < BlockAligner.DefaultThreshold,
            $"GET /users and GET /orders are different operations; scored {score:0.000}");
    }

    [Fact]
    public void Similarity_SameRouteAndIdButDifferentMethod_StaysBelowThreshold()
    {
        // An APIM operation is identified by (method, url_template). A GET and a
        // POST on the same path are different operations — pairing them would
        // hide a genuinely new operation during a merge.
        var a = TestOps.Op("POST", "users", "manage-users");
        var b = TestOps.Op("GET", "users", "manage-users");

        var score = SimilarityScorer.Similarity(a, b);
        Assert.True(score < BlockAligner.DefaultThreshold,
            $"POST and GET /users are different operations; scored {score:0.000}");
    }

    [Fact]
    public void Similarity_SubRouteOfAnotherOperation_StaysBelowThreshold()
    {
        // A deeper route is a different url_template, so a different operation.
        // Text similarity alone puts these at 0.72 — the segment-count gate is
        // what separates them, since a legitimate rename (v1/users vs v2/users)
        // scores no higher.
        var a = TestOps.Op("GET", "stock/{sku}");
        var b = TestOps.Op("GET", "stock/{sku}/history");

        var score = SimilarityScorer.Similarity(a, b);
        Assert.True(score < BlockAligner.DefaultThreshold,
            $"a sub-route is a distinct operation; scored {score:0.000}");
    }

    [Fact]
    public void Similarity_VersionedRouteRename_StillMatches()
    {
        // The counterpart to the segment-count gate: same depth, one segment
        // renamed, so these must remain matchable.
        var a = TestOps.Op("GET", "v1/users");
        var b = TestOps.Op("GET", "v2/users");

        Assert.True(SimilarityScorer.Similarity(a, b) >= BlockAligner.DefaultThreshold,
            "a version-prefix rename at equal depth should still align");
    }

    [Fact]
    public void Similarity_UnrelatedRoutes_Low()
    {
        var a = TestOps.Op("GET", "users");
        var b = TestOps.Op("GET", "invoices/{id}/lines");
        Assert.True(SimilarityScorer.Similarity(a, b) < 0.55, "unrelated routes should fall below the match threshold");
    }

    [Fact]
    public void OperationIdSimilarity_ToleratesInterpolations()
    {
        // Env differs but the interpolation structure is identical.
        Assert.Equal(1.0, SimilarityScorer.OperationIdSimilarity("${op}-${env}", "${op}-${env}"), 6);
        Assert.True(SimilarityScorer.OperationIdSimilarity("${prefix}-${env}", "get-users-dev") < 1.0);
    }

    [Fact]
    public void UrlSimilarity_MatchedByParametersAndSegments()
    {
        var high = SimilarityScorer.UrlSimilarity(
            TestOps.Op("GET", "orders/{id}"), TestOps.Op("GET", "orders/:id"));
        Assert.True(high > 0.9, $"brace vs colon param should be near-identical, got {high}");
    }
}
