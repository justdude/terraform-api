using System.Runtime.Versioning;
using TerraformMerge.Engine;
using TerraformMerge.Ui;

namespace TerraformMerge.Tests;

/// <summary>
/// Constructs the WinForms controls and forces native handle creation on an STA
/// thread. This exercises the whole layout tree (data-source bindings, table
/// layout row/column counts, the editor grid) — a mismatch there throws at
/// handle creation, so these catch UI wiring regressions the logic tests can't.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiSmokeTests
{
    private static Exception? RunSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return captured;
    }

    [Fact]
    public void MainForm_BuildsControlTree_WithoutError()
    {
        var ex = RunSta(() =>
        {
            using var form = new MainForm();
            _ = form.Handle; // forces creation of the entire control tree
        });
        Assert.Null(ex);
    }

    [Fact]
    public void OperationEditorDialog_BuildsEveryFieldRow_WithoutError()
    {
        var ex = RunSta(() =>
        {
            var subject = new OperationNode
            {
                Source = OperationSource.OriginalTerraform,
                OperationId = "list-orders-dev",
                Method = "GET",
                UrlTemplate = "orders",
                ApiName = "orders-api-dev",
                ApimResourceGroupName = "rg-apim-dev"
            };
            var catalog = MergeFieldCatalog.Build(
                [subject],
                [new OperationNode { Source = OperationSource.TargetTerraform, Method = "GET", UrlTemplate = "orders", ApimResourceGroupName = "rg-apim-staging" }]);

            using var dialog = new OperationEditorDialog(subject, catalog);
            _ = dialog.Handle;
        });
        Assert.Null(ex);
    }
}
