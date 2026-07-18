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
