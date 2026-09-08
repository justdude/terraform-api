using TerraformApi.Application;
using TerraformApi.Domain.Models;
using TerraformMerge.Engine;
using TerraformMerge.IntegrationTests.Common;

namespace TerraformMerge.IntegrationTests.Workflows;

/// <summary>
/// End-to-end tests of the merge engine through real file I/O, mirroring exactly
/// what each GUI action does (load a file → compute diff → add/remove → edit →
/// save to disk → reload). These are the app's features exercised through the
/// same code paths the window invokes, minus the WinForms plumbing.
/// </summary>
public class MergeWorkflowTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly MergeEngine _merge = new();
    private readonly OriginalWriter _writer = new();

    private LoadedOperations LoadOriginal() =>
        _loader.LoadTerraform(TestEnvironment.FixtureText("original.tf"), OperationSource.OriginalTerraform);

    private LoadedOperations LoadTargetTerraform() =>
        _loader.Load(TestEnvironment.FixtureText("target.tf"), OperationSource.TargetTerraform, OperationSource.TargetOpenApi, "target.tf");

    private LoadedOperations LoadTargetOpenApi() =>
        _loader.LoadOpenApi(TestEnvironment.FixtureText("target-openapi.json"), OperationSource.TargetOpenApi, "target-openapi.json");

    [Fact] // F13
    public void LoadOriginal_ListsOperations()
    {
        var original = LoadOriginal();
        Assert.Equal(2, original.Nodes.Count);
        Assert.All(original.Nodes, n => Assert.Equal("orders-api-group", n.ApiGroupName));
    }

    [Fact] // F14
    public void LoadTarget_TerraformAndOpenApi()
    {
        Assert.Equal(2, LoadTargetTerraform().Nodes.Count);

        var api = LoadTargetOpenApi();
        Assert.Equal(["DELETE", "GET"], api.Nodes.Select(n => n.Method).OrderBy(m => m).ToList());
    }

    [Fact] // F15
    public void ComputeDiff_ClassifiesAddedAndOmitsEquivalent()
    {
        var diff = _merge.ComputeDiff(LoadOriginal().Nodes, LoadTargetTerraform().Nodes);
        var added = diff.Where(d => d.Kind == DiffKind.Added).ToList();
        // list-orders is equivalent (GET orders) → omitted; create POST → added.
        Assert.Single(added);
        Assert.Equal("POST", added[0].Operation.Method);
    }

    [Fact] // F15b — API mode diff
    public void ComputeDiff_ApiMode_NewMethodOnSameRouteIsAdded()
    {
        var diff = _merge.ComputeDiff(LoadOriginal().Nodes, LoadTargetOpenApi().Nodes);
        Assert.Contains(diff, d => d.Kind == DiffKind.Added
            && d.Operation.Method == "DELETE" && d.Operation.UrlTemplate == "orders/{orderId}");
    }

    [Fact] // F16
    public void AddToOriginal_SkipsEquivalentsAlreadyPresent()
    {
        var original = LoadOriginal();
        var merged = _merge.AppendMissing(original.Nodes, LoadTargetTerraform().Nodes);
        Assert.Equal(3, merged.Count); // 2 originals + create; list not duplicated
    }

    [Fact] // F17
    public void RemoveFromOriginal_DropsSelectedOperation()
    {
        var original = LoadOriginal();
        var kept = original.Nodes.Where(n => n.OperationId != "get-order-dev").ToList();

        var reparsed = _loader.LoadTerraform(_writer.Rewrite(original, kept), OperationSource.OriginalTerraform);

        Assert.Single(reparsed.Nodes);
        Assert.DoesNotContain(reparsed.Nodes, n => n.OperationId == "get-order-dev");
    }

    [Fact] // F18
    public void SaveOriginal_ToDisk_AppendsAndPreservesOriginalsVerbatim()
    {
        using var ws = new TempWorkspace();
        var original = LoadOriginal();
        var desired = _merge.AppendMissing(original.Nodes, LoadTargetTerraform().Nodes);

        var outPath = ws.Write("merged.tf", _writer.Rewrite(original, desired));

        // Original operations survive byte-for-byte; the file reloads with 3 ops.
        var text = File.ReadAllText(outPath);
        Assert.Contains("operation_id             = \"list-orders-dev\"", text);
        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(3, reparsed.Nodes.Count);
    }

    [Fact] // F19
    public void EditOperation_AcceptValueFromOtherSide_ChangesExactlyOneLine()
    {
        var original = LoadOriginal();
        var target = LoadTargetTerraform();
        var catalog = MergeFieldCatalog.Build(original.Nodes, target.Nodes);

        // The other side's resource group is an available choice.
        Assert.Contains("rg-apim-staging", catalog.Values(OperationField.ApimResourceGroupName));

        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");
        OperationEditor.Apply(list, new Dictionary<OperationField, string>
        {
            [OperationField.ApimResourceGroupName] = "rg-apim-staging"
        });

        var before = TestEnvironment.FixtureText("original.tf").Replace("\r\n", "\n").Split('\n');
        var after = _writer.Rewrite(original, original.Nodes.ToList()).Replace("\r\n", "\n").Split('\n');

        Assert.Equal(before.Length, after.Length);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        var i = Assert.Single(changed);
        Assert.Contains("rg-apim-staging", after[i]);
        // operation_id is never touched.
        Assert.Equal("list-orders-dev",
            _loader.LoadTerraform(string.Join("\n", after), OperationSource.OriginalTerraform)
                   .Nodes.First().OperationId);
    }

    [Fact] // F20
    public void EditOperation_QuoteAndInterpolation_ProducesParseableHcl()
    {
        var original = LoadOriginal();
        var op = original.Nodes.First();
        OperationEditor.Apply(op, new Dictionary<OperationField, string>
        {
            [OperationField.Description] = @"the ""${var.env}"" one \ here"
        });

        var reparsed = _loader.LoadTerraform(_writer.Rewrite(original, original.Nodes.ToList()), OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count); // still valid HCL
    }

    [Fact] // F22
    public void MultiGroup_RewriteUnchanged_IsByteForByte_AndEditIsolated()
    {
        var doc = _loader.LoadTerraform(TestEnvironment.FixtureText("multigroup.tf"), OperationSource.OriginalTerraform);

        // Unchanged rewrite is byte-for-byte.
        Assert.Equal(TestEnvironment.FixtureText("multigroup.tf"), _writer.Rewrite(doc, doc.Nodes.ToList()));

        // Editing the orders op leaves the inventory group intact.
        var orders = doc.Nodes.Single(n => n.ApiGroupName == "orders-api-group");
        OperationEditor.Apply(orders, new Dictionary<OperationField, string> { [OperationField.DisplayName] = "All orders" });
        var text = _writer.Rewrite(doc, doc.Nodes.ToList());

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count);
        Assert.Single(reparsed.Nodes, n => n.ApiGroupName == "inventory-api-group");
        Assert.Contains("list-stock-dev", text);
    }

    [Fact] // F23
    public void MergeFieldCatalog_UnionsBothSides()
    {
        var catalog = MergeFieldCatalog.Build(LoadOriginal().Nodes, LoadTargetTerraform().Nodes);
        Assert.Equal(["rg-apim-dev", "rg-apim-staging"], catalog.Values(OperationField.ApimResourceGroupName));
    }

    [Fact] // F24
    public void Convert_Facade_ProducesConfigFromOpenApi()
    {
        var facade = TerraformApiFacade.Create();
        var result = facade.ConvertOpenApiToTerraform(
            TestEnvironment.FixtureText("convert-input.json"),
            new ConversionSettings { Environment = "dev", ApiGroupName = "catalog-api-group" });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("api_operations", result.TerraformConfig);
        Assert.Contains("products", result.TerraformConfig);
    }
}
