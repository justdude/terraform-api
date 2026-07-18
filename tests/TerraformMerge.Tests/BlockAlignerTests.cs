using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class BlockAlignerTests
{
    [Fact]
    public void MaxWeightAssignment_ChoosesGlobalOptimum_NotGreedy()
    {
        // Greedy would take A-X (0.90) then be forced into B-Y (0.10) = 1.00.
        // Optimal is A-Y (0.80) + B-X (0.85) = 1.65.
        var weights = new[]
        {
            new[] { 0.90, 0.80 }, // A
            new[] { 0.85, 0.10 }  // B
        };

        var assignment = BlockAligner.MaxWeightAssignment(weights)
            .OrderBy(p => p.Row)
            .ToList();

        Assert.Equal([(0, 1), (1, 0)], assignment);
    }

    [Fact]
    public void MaxWeightAssignment_Rectangular_MoreLeftThanRight()
    {
        var weights = new[]
        {
            new[] { 0.9 },
            new[] { 0.2 },
            new[] { 0.5 }
        };

        var assignment = BlockAligner.MaxWeightAssignment(weights).ToList();

        // Exactly one column can be assigned; it goes to the best row.
        Assert.Single(assignment);
        Assert.Equal((0, 0), assignment[0]);
    }

    [Fact]
    public void Align_ThresholdDemotesWeakPairs()
    {
        var left = new[] { TestOps.Op("GET", "users", source: OperationSource.OriginalTerraform) };
        var right = new[] { TestOps.Op("DELETE", "invoices/{id}") };

        var result = BlockAligner.Align(left, right, threshold: 0.55);

        Assert.Empty(result.Matched);
        Assert.Equal([0], result.UnmatchedLeft);
        Assert.Equal([0], result.UnmatchedRight);
    }

    [Fact]
    public void Align_MatchesEquivalentRoutesAcrossEnvironments()
    {
        var left = new[]
        {
            TestOps.Op("GET", "users", "list-users-dev", OperationSource.OriginalTerraform),
            TestOps.Op("POST", "users", "create-user-dev", OperationSource.OriginalTerraform)
        };
        var right = new[]
        {
            TestOps.Op("POST", "users", "createUser"),
            TestOps.Op("GET", "users", "listUsers")
        };

        var result = BlockAligner.Align(left, right);

        Assert.Equal(2, result.Matched.Count);
        Assert.Empty(result.UnmatchedLeft);
        Assert.Empty(result.UnmatchedRight);
        // GET-left(0) pairs with GET-right(1); POST-left(1) with POST-right(0).
        Assert.Contains(result.Matched, m => m.LeftIndex == 0 && m.RightIndex == 1);
        Assert.Contains(result.Matched, m => m.LeftIndex == 1 && m.RightIndex == 0);
    }

    [Fact]
    public void Align_EmptyInputs_AllUnmatched()
    {
        var right = new[] { TestOps.Op("GET", "x") };
        var result = BlockAligner.Align([], right);

        Assert.Empty(result.Matched);
        Assert.Empty(result.UnmatchedLeft);
        Assert.Equal([0], result.UnmatchedRight);
    }
}
