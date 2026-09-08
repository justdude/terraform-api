using TerraformMerge.Engine;
using TerraformMerge.IntegrationTests.Common;

namespace TerraformMerge.IntegrationTests.Workflows;

/// <summary>
/// The environment workflow end to end: one document is dev, the other qa, and
/// the qa document is given the operations it is missing from dev — as qa
/// operations, not dev ones pasted into a qa file. This is what the panes'
/// environment pickers drive; the code path is the same one the buttons call.
/// </summary>
public class EnvironmentWorkflowTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly MergeEngine _merge = new();
    private readonly OriginalWriter _writer = new();

    private LoadedOperations LoadQa() =>
        _loader.LoadTerraform(TestEnvironment.FixtureText("qa.tf"), OperationSource.OriginalTerraform);

    private LoadedOperations LoadDev() =>
        _loader.LoadTerraform(TestEnvironment.FixtureText("original.tf"), OperationSource.TargetTerraform);

    [Fact]
    public void LoadedDocumentsReportTheirEnvironments()
    {
        Assert.Equal("qa", EnvironmentCatalog.Dominant(LoadQa().Nodes));
        Assert.Equal("dev", EnvironmentCatalog.Dominant(LoadDev().Nodes));

        var catalog = EnvironmentCatalog.Build(LoadQa().Nodes, LoadDev().Nodes);
        Assert.Equal(["dev", "qa"], catalog.Names);
        Assert.Equal("rg-apim-qa", catalog.Profile("qa")!.ResourceGroup);
        Assert.Equal("apim-company-dev", catalog.Profile("dev")!.ApimName);
    }

    [Fact]
    public void MissingDevOperationsAreAddedToQaAsQaOperations()
    {
        using var ws = new TempWorkspace();
        var qa = LoadQa();
        var dev = LoadDev();
        var catalog = EnvironmentCatalog.Build(qa.Nodes, dev.Nodes);

        // What the Diff pane lists: dev has an operation qa does not.
        var missing = _merge.ComputeDiff(qa.Nodes, dev.Nodes)
            .Where(entry => entry.Kind == DiffKind.Added)
            .Select(entry => entry.Operation)
            .ToList();
        var incoming = Assert.Single(missing);
        Assert.Equal("get-order-dev", incoming.OperationId);

        // What "◄ Add to Original" does with qa chosen on the Original pane.
        var copy = incoming.Copy();
        Assert.True(EnvironmentRetargeter.Retarget(copy, "qa", catalog));

        var desired = qa.Nodes.Concat([copy]).ToList();
        var outPath = ws.Write("qa.tf", _writer.Rewrite(qa, desired));
        var saved = File.ReadAllText(outPath);

        var reparsed = _loader.LoadTerraform(saved, OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count);

        var added = reparsed.Nodes.Single(node => node.UrlTemplate == "orders/{orderId}");
        Assert.Equal("get-order-qa", added.OperationId);
        Assert.Equal("rg-apim-qa", added.ApimResourceGroupName);
        Assert.Equal("apim-company-qa", added.ApimName);
        Assert.Equal("orders-api-qa", added.ApiName);
        Assert.Equal("qa", added.Environment);

        // Nothing dev-shaped reached the qa file, and the operation it already
        // had is still there byte-for-byte.
        Assert.DoesNotContain("-dev", saved);
        Assert.Contains("operation_id             = \"list-orders-qa\"", saved);
    }

    [Fact]
    public void SettingAnOperationsEnvironmentInPlaceRewritesOnlyThatOperation()
    {
        // The Original pane's "Set selected": one operation of the dev file is
        // moved to qa and saved. Its four environment-bearing lines change; the
        // rest of the file, the other operation included, does not.
        var dev = _loader.LoadTerraform(TestEnvironment.FixtureText("original.tf"), OperationSource.OriginalTerraform);
        var subject = dev.Nodes.Single(node => node.OperationId == "get-order-dev");

        Assert.True(EnvironmentRetargeter.Retarget(subject, "qa", EnvironmentCatalog.Build(dev.Nodes, [])));

        var before = TestEnvironment.FixtureText("original.tf").Replace("\r\n", "\n").Split('\n');
        var after = _writer.Rewrite(dev, dev.Nodes.ToList()).Replace("\r\n", "\n").Split('\n');

        Assert.Equal(before.Length, after.Length);
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToList();
        Assert.Equal(4, changed.Count); // operation_id, resource group, apim, api name
        Assert.All(changed, i => Assert.Contains("qa", after[i]));

        var reparsed = _loader.LoadTerraform(string.Join("\n", after), OperationSource.OriginalTerraform);
        Assert.Equal("get-order-qa", reparsed.Nodes.Single(n => n.UrlTemplate == "orders/{orderId}").OperationId);
        Assert.Equal("list-orders-dev", reparsed.Nodes.Single(n => n.UrlTemplate == "orders").OperationId);
    }
}
