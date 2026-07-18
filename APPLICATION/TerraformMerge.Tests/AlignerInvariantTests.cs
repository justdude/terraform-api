using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// Property / invariant tests over randomized inputs (fixed seed → deterministic):
/// structural invariants of <see cref="BlockAligner.Align"/>, similarity-score
/// bounds and symmetry, and — the key correctness guarantee — that the Hungarian
/// solver's total weight equals the brute-force optimum on small matrices.
/// </summary>
public class AlignerInvariantTests
{
    private static readonly string[] Methods = ["GET", "POST", "PUT", "DELETE", "PATCH"];
    private static readonly string[] Urls =
    [
        "users", "users/{id}", "users/{id}/orders", "orders", "orders/{orderId}",
        "invoices", "invoices/{id}/lines", "health", "accounts/{id}/transactions"
    ];
    private static readonly string[] Params = ["query:limit", "query:status", "header:authorization", "query:page"];

    private static OperationNode RandomOp(Random rng, OperationSource source)
    {
        var paramCount = rng.Next(0, 3);
        var paramKeys = Enumerable.Range(0, paramCount)
            .Select(_ => Params[rng.Next(Params.Length)])
            .Distinct()
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        return new OperationNode
        {
            Source = source,
            Method = Methods[rng.Next(Methods.Length)],
            UrlTemplate = Urls[rng.Next(Urls.Length)],
            OperationId = rng.Next(2) == 0 ? $"op{rng.Next(1000)}-dev" : "",
            ParameterKeys = paramKeys
        };
    }

    [Fact]
    public void Align_StructuralInvariants_HoldOverManyRandomInputs()
    {
        var rng = new Random(20260516);

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var left = Enumerable.Range(0, rng.Next(0, 7)).Select(_ => RandomOp(rng, OperationSource.OriginalTerraform)).ToList();
            var right = Enumerable.Range(0, rng.Next(0, 7)).Select(_ => RandomOp(rng, OperationSource.TargetTerraform)).ToList();

            var result = BlockAligner.Align(left, right);

            // Every left index is accounted for exactly once (matched XOR unmatched).
            var matchedLeft = result.Matched.Select(m => m.LeftIndex).ToList();
            var matchedRight = result.Matched.Select(m => m.RightIndex).ToList();

            Assert.Equal(left.Count, matchedLeft.Count + result.UnmatchedLeft.Count);
            Assert.Equal(right.Count, matchedRight.Count + result.UnmatchedRight.Count);

            // No index used twice on either side.
            Assert.Equal(matchedLeft.Count, matchedLeft.Distinct().Count());
            Assert.Equal(matchedRight.Count, matchedRight.Distinct().Count());
            Assert.Empty(matchedLeft.Intersect(result.UnmatchedLeft));
            Assert.Empty(matchedRight.Intersect(result.UnmatchedRight));

            // Indices are in range and matched count is bounded.
            Assert.True(result.Matched.Count <= Math.Min(left.Count, right.Count));
            Assert.All(result.Matched, m =>
            {
                Assert.InRange(m.LeftIndex, 0, left.Count - 1);
                Assert.InRange(m.RightIndex, 0, right.Count - 1);
                Assert.True(m.Similarity >= BlockAligner.DefaultThreshold);
                Assert.InRange(m.Similarity, 0.0, 1.0);
            });

            // Reported similarity equals a fresh computation for the pair.
            Assert.All(result.Matched, m =>
                Assert.Equal(SimilarityScorer.Similarity(left[m.LeftIndex], right[m.RightIndex]), m.Similarity, 9));
        }
    }

    [Fact]
    public void Similarity_IsSymmetric_AndInUnitRange()
    {
        var rng = new Random(4242);
        for (var i = 0; i < 3000; i++)
        {
            var a = RandomOp(rng, OperationSource.OriginalTerraform);
            var b = RandomOp(rng, OperationSource.TargetTerraform);

            var ab = SimilarityScorer.Similarity(a, b);
            var ba = SimilarityScorer.Similarity(b, a);

            Assert.Equal(ab, ba, 9);
            Assert.InRange(ab, 0.0, 1.0);
        }
    }

    [Fact]
    public void MaxWeightAssignment_EqualsBruteForceOptimum_OnSmallMatrices()
    {
        var rng = new Random(7);

        for (var trial = 0; trial < 400; trial++)
        {
            var rows = rng.Next(1, 6);
            var cols = rng.Next(1, 6);
            var weights = new double[rows][];
            for (var i = 0; i < rows; i++)
            {
                weights[i] = new double[cols];
                for (var j = 0; j < cols; j++)
                    weights[i][j] = Math.Round(rng.NextDouble(), 3);
            }

            var hungarian = BlockAligner.MaxWeightAssignment(weights).Sum(p => weights[p.Row][p.Col]);
            var optimal = BruteForceMaxAssignment(weights);

            Assert.Equal(optimal, hungarian, 6);
        }
    }

    /// <summary>Exhaustive maximum-weight assignment for small matrices (reference oracle).</summary>
    private static double BruteForceMaxAssignment(double[][] weights)
    {
        var rows = weights.Length;
        var cols = weights[0].Length;
        var best = 0.0;

        // Assign each row to a distinct column (or to none), maximize total weight.
        void Recurse(int row, bool[] usedCol, double acc)
        {
            best = Math.Max(best, acc);
            if (row >= rows)
                return;

            // Option: leave this row unassigned.
            Recurse(row + 1, usedCol, acc);

            // Option: assign this row to some free column.
            for (var c = 0; c < cols; c++)
            {
                if (usedCol[c])
                    continue;
                usedCol[c] = true;
                Recurse(row + 1, usedCol, acc + weights[row][c]);
                usedCol[c] = false;
            }
        }

        Recurse(0, new bool[cols], 0.0);
        return best;
    }
}
