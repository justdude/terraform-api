using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// End-to-end tests over <c>samples/azure-apim-platform.tf</c> — the realistic
/// Azure APIM config shipped for manual testing. The sample is linked into the
/// test output rather than copied, so the file a user loads in the app is
/// exactly the one asserted on here.
///
/// It is deliberately harder than <c>sample-apim.tf</c>: two api groups,
/// ${var.…} interpolations throughout, an indented (&lt;&lt;-) policy heredoc,
/// interleaved comments, and near-miss operations. Each of those broke
/// something real when it was first loaded.
/// </summary>
public class AzureApimPlatformSampleTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly MergeEngine _merge = new();
    private readonly OriginalWriter _writer = new();

    private const string SampleName = "azure-apim-platform.tf";

    private static string Sample() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "samples", SampleName));

    /// <summary>
    /// Rewriting mutates the AST in place, so every test that writes starts from
    /// a freshly parsed document.
    /// </summary>
    private LoadedOperations Load() =>
        _loader.LoadTerraform(Sample(), OperationSource.OriginalTerraform);

    [Fact]
    public void Load_ReadsBothApiGroups()
    {
        var loaded = Load();

        Assert.Equal(8, loaded.Nodes.Count);
        Assert.Equal(5, loaded.Nodes.Count(n => n.ApiGroupName == "payments-api-group"));
        Assert.Equal(3, loaded.Nodes.Count(n => n.ApiGroupName == "inventory-api-group"));
        Assert.Equal(["apis", "bpc_apis", "backend_apis"], loaded.TerraformDocument!.ApiGroupParentPath);
    }

    [Fact]
    public void Load_IndentedPolicyHeredoc_MethodTagsAreNotOperations()
    {
        // The <<- policy lists <method>GET</method>…<method>DELETE</method>.
        // Only the eight real api_operations may appear.
        var loaded = Load();

        Assert.Equal(["GET", "PATCH", "POST", "PUT"],
            loaded.Nodes.Select(n => n.Method).Distinct().Order().ToList());
        Assert.Equal(8, loaded.Nodes.Count);
        Assert.DoesNotContain(loaded.Nodes, n => string.IsNullOrEmpty(n.UrlTemplate));
    }

    [Fact]
    public void Load_InterpolatedOperationIdsAndParametersSurvive()
    {
        var loaded = Load();

        var list = loaded.Nodes.Single(n => n.UrlTemplate == "v1/payments" && n.Method == "GET");
        Assert.Equal("${var.operation_prefix}-list-payments-${var.environment}", list.OperationId);
        Assert.Equal(["header:accept", "query:from", "query:limit", "query:status"], list.ParameterKeys);
        Assert.Equal([200, 400], list.ResponseCodes);

        var create = loaded.Nodes.Single(n => n.Method == "POST");
        Assert.Equal(["header:authorization", "header:idempotency-key"], create.ParameterKeys);
    }

    [Fact]
    public void Rewrite_Unchanged_IsByteForByteIdentical()
    {
        // Regression: with two api groups the writer used to funnel every
        // operation into one group, dirtying its array and re-rendering the
        // whole document even when nothing had changed.
        var loaded = Load();

        var text = _writer.Rewrite(loaded, loaded.Nodes.ToList());

        Assert.Equal(Sample(), text);
    }

    [Fact]
    public void Merge_MultipleGroups_DoesNotDuplicateTheOtherGroupsOperations()
    {
        // Regression: all eight operations were written into payments-api-group
        // while inventory-api-group kept its own three, so a save silently
        // duplicated inventory's operations into the payments API.
        var loaded = Load();
        var target = new[] { TestOps.Op("DELETE", "v1/payments/{paymentId}", "deletePayment") };

        var desired = _merge.AppendMissing(loaded.Nodes, target);
        Assert.Equal(9, desired.Count);

        var reparsed = _loader.LoadTerraform(_writer.Rewrite(loaded, desired), OperationSource.OriginalTerraform);

        Assert.Equal(9, reparsed.Nodes.Count);
        Assert.Equal(6, reparsed.Nodes.Count(n => n.ApiGroupName == "payments-api-group"));
        Assert.Equal(3, reparsed.Nodes.Count(n => n.ApiGroupName == "inventory-api-group"));
        Assert.Single(reparsed.Nodes, n => n.Method == "DELETE" && n.ApiGroupName == "payments-api-group");
    }

    [Fact]
    public void Merge_GeneratedBlock_CarriesInterpolationsInsteadOfPlaceholders()
    {
        // Regression: apim_name was read only as a literal, so an interpolated
        // config produced apim_name = "{apim-name}" — an invalid value dropped
        // into an otherwise valid file — while the sibling fields interpolated
        // correctly.
        var loaded = Load();
        var target = new[] { TestOps.Op("DELETE", "v1/payments/{paymentId}", "deletePayment") };

        var text = _writer.Rewrite(loaded, _merge.AppendMissing(loaded.Nodes, target));

        var block = text.Replace("\r\n", "\n").Split('\n')
            .SkipWhile(l => !l.Contains("\"deletePayment\""))
            .Take(4)
            .ToList();

        Assert.Contains(block, l => l.Contains("apim_name") && l.Contains("${var.apim_name}"));
        Assert.Contains(block, l => l.Contains("apim_resource_group_name") && l.Contains("${var.stage_group_name}"));
        Assert.DoesNotContain(text, "{apim-name}");
    }

    [Fact]
    public void Remove_FromOneGroup_LeavesTheOtherGroupUntouched()
    {
        var loaded = Load();
        var kept = loaded.Nodes.Where(n => n.OperationId != "adjust-stock-item").ToList();

        var reparsed = _loader.LoadTerraform(_writer.Rewrite(loaded, kept), OperationSource.OriginalTerraform);

        Assert.Equal(7, reparsed.Nodes.Count);
        Assert.Equal(5, reparsed.Nodes.Count(n => n.ApiGroupName == "payments-api-group"));
        Assert.DoesNotContain(reparsed.Nodes, n => n.OperationId == "adjust-stock-item");
        // The payments group was not re-rendered, so its policy heredoc is intact.
        Assert.Contains("<method>DELETE</method>", _writer.Rewrite(Load(), kept));
    }

    [Fact]
    public void Merge_SubRouteOfAnExistingOperation_IsOfferedAsAnAddition()
    {
        // Regression: GET stock/{sku}/history scored 0.72 against the existing
        // GET stock/{sku} — above the match threshold — so adding a sub-route
        // was reported as "already present" and silently dropped.
        var loaded = Load();
        var target = new[] { TestOps.Op("GET", "stock/{sku}/history", "stockHistory") };

        var diff = _merge.ComputeDiff(loaded.Nodes, target);

        Assert.Single(diff, d => d.Kind == DiffKind.Added && d.Operation.UrlTemplate == "stock/{sku}/history");
    }

    [Fact]
    public void Merge_NearMissOperations_AreNotCollapsed()
    {
        // v1/payments vs v2/payments (same method) and GET vs PUT on
        // v1/payments/{paymentId} are distinct operations. Offering the whole
        // sample back as the target must therefore add nothing at all.
        var loaded = Load();
        var target = Load(); // same operations, as if read from another file

        var diff = _merge.ComputeDiff(loaded.Nodes, target.Nodes);

        Assert.DoesNotContain(diff, d => d.Kind == DiffKind.Added);
    }

    [Fact]
    public void Merge_IsConvergent_SecondPassAddsNothing()
    {
        var target = new[]
        {
            TestOps.Op("DELETE", "v1/payments/{paymentId}", "deletePayment"),
            TestOps.Op("GET", "stock/{sku}/history", "stockHistory")
        };

        var first = _writer.Rewrite(Load(), _merge.AppendMissing(Load().Nodes, target));

        var reloaded = _loader.LoadTerraform(first, OperationSource.OriginalTerraform);
        Assert.Equal(10, reloaded.Nodes.Count);

        var second = _merge.AppendMissing(reloaded.Nodes, target);
        Assert.Equal(reloaded.Nodes.Count, second.Count);
    }
}
