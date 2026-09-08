using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows.Forms;
using TerraformMerge.Engine;
using TerraformMerge.IntegrationTests.Common;
using TerraformMerge.Ui;

namespace TerraformMerge.IntegrationTests.Ui;

/// <summary>
/// UI wiring exercised against a real MainForm on an STA thread, through its own
/// handler methods (via <see cref="FormDriver"/>). Only non-modal paths are
/// driven — anything that opens a dialog or MessageBox would block headlessly.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("ui")]
public class MainFormDriverTests
{
    [Fact] // F25
    public void MainForm_BuildsControlTree_WithoutError()
    {
        FormDriver.Run(_ => { /* construction + Handle in FormDriver.Run */ });
    }

    [Fact] // F26
    public void OperationEditorDialog_BuildsWithoutError()
    {
        FormDriver.RunSta(() =>
        {
            var subject = new OperationNode
            {
                Source = OperationSource.OriginalTerraform,
                OperationId = "list-orders-dev",
                Method = "GET",
                UrlTemplate = "orders",
                ApimResourceGroupName = "rg-apim-dev"
            };
            var catalog = MergeFieldCatalog.Build(
                [subject],
                [new OperationNode { Source = OperationSource.TargetTerraform, Method = "GET", UrlTemplate = "orders", ApimResourceGroupName = "rg-apim-staging" }]);
            using var dialog = new OperationEditorDialog(subject, catalog);
            _ = dialog.Handle;
        });
    }

    [Fact] // F27
    public void ModeToggle_UpdatesTargetPaneTitle()
    {
        FormDriver.Run(form =>
        {
            FormDriver.Field<RadioButton>(form, "_modeApi").Checked = true;
            Assert.Equal("Target (OpenAPI)", FormDriver.Field<GroupBox>(form, "_grpTarget").Text);

            FormDriver.Field<RadioButton>(form, "_modeMerge").Checked = true;
            Assert.Equal("Target", FormDriver.Field<GroupBox>(form, "_grpTarget").Text);
        });
    }

    [Fact] // F28
    public void LoadComputeDiffSave_ThroughFormHandlers()
    {
        FormDriver.Run(form =>
        {
            using var ws = new TempWorkspace();
            var originalPath = ws.CopyFixture("original.tf");

            FormDriver.SetText(form, "_pathOriginal", originalPath);
            FormDriver.Invoke(form, "LoadOriginal", null, EventArgs.Empty);
            Assert.Equal(2, FormDriver.Field<BindingList<OperationNode>>(form, "_original").Count);

            FormDriver.SetText(form, "_pathTarget", TestEnvironment.FixturePath("target.tf"));
            FormDriver.Invoke(form, "LoadTarget", null, EventArgs.Empty);
            Assert.Equal(2, FormDriver.Field<BindingList<OperationNode>>(form, "_target").Count);

            FormDriver.Invoke(form, "ComputeDiff");
            Assert.True(FormDriver.Field<BindingList<DiffEntry>>(form, "_diff").Count >= 1);

            // Save writes the loaded document back to the same path (no guard dialog).
            FormDriver.Invoke(form, "SaveOriginal");
            var reloaded = new OperationSourceLoader()
                .LoadTerraform(File.ReadAllText(originalPath), OperationSource.OriginalTerraform);
            Assert.Equal(2, reloaded.Nodes.Count); // unchanged originals persisted
        });
    }
}
