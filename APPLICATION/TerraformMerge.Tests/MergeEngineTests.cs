using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class MergeEngineTests
{
    private readonly MergeEngine _merge = new();

    [Fact]
    public void ComputeDiff_ClassifiesAddedAndChanged_OmitsIdentical()
    {
        var original = new[]
        {
            TestOps.Op("GET", "users", "list-dev", OperationSource.OriginalTerraform),
            TestOps.Op("PUT", "users/{id}", "update-dev", OperationSource.OriginalTerraform, "header:authorization")
        };
        var target = new[]
        {
            TestOps.Op("GET", "users", "listUsers"),                              // identical → omitted
            TestOps.Op("PUT", "users/{id}", "updateUser", parameterKeys: "header:x-trace"), // changed params
            TestOps.Op("POST", "users", "createUser")                             // added
        };

        var diff = _merge.ComputeDiff(original, target);

        Assert.DoesNotContain(diff, d => d.Operation.Method == "GET");
        var changed = Assert.Single(diff, d => d.Kind == DiffKind.Changed);
        Assert.Equal("PUT", changed.Operation.Method);
        var added = Assert.Single(diff, d => d.Kind == DiffKind.Added);
        Assert.Equal("POST", added.Operation.Method);
    }

    [Fact]
    public void AppendMissing_KeepsOriginalAndAddsUnmatched()
    {
        var original = new[]
        {
            TestOps.Op("GET", "users", "list-dev", OperationSource.OriginalTerraform)
        };
        var target = new[]
        {
            TestOps.Op("GET", "users", "listUsers"),      // matched → not added
            TestOps.Op("POST", "users", "createUser"),    // added
            TestOps.Op("DELETE", "users/{id}", "deleteUser") // added
        };

        var merged = _merge.AppendMissing(original, target);

        Assert.Equal(3, merged.Count);
        Assert.Same(original[0], merged[0]);
        Assert.Contains(merged, n => n.Method == "POST");
        Assert.Contains(merged, n => n.Method == "DELETE");
    }

    [Fact]
    public void AppendMissing_EmptyTarget_ReturnsOriginalUnchanged()
    {
        var original = new[] { TestOps.Op("GET", "users", source: OperationSource.OriginalTerraform) };
        var merged = _merge.AppendMissing(original, []);
        Assert.Single(merged);
    }

    [Fact]
    public void AreEquivalent_ComparesMethodUrlAndParameters()
    {
        var a = TestOps.Op("GET", "/users/", "x", parameterKeys: "query:limit");
        var b = TestOps.Op("get", "users", "y", parameterKeys: "query:limit");
        Assert.True(MergeEngine.AreEquivalent(a, b));

        var c = TestOps.Op("GET", "users", parameterKeys: "query:offset");
        Assert.False(MergeEngine.AreEquivalent(a, c));
    }
}
