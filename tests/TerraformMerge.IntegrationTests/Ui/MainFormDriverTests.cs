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

    [Fact] // F29
    public void LoadingAPane_PreselectsTheEnvironmentTheDocumentIsIn()
    {
        FormDriver.Run(form =>
        {
            FormDriver.SetText(form, "_pathOriginal", TestEnvironment.FixturePath("qa.tf"));
            FormDriver.Invoke(form, "LoadOriginal", null, EventArgs.Empty);
            Assert.Equal("qa", FormDriver.Field<ComboBox>(form, "_envOriginal").Text);

            FormDriver.SetText(form, "_pathTarget", TestEnvironment.FixturePath("original.tf"));
            FormDriver.Invoke(form, "LoadTarget", null, EventArgs.Empty);
            Assert.Equal("dev", FormDriver.Field<ComboBox>(form, "_envTarget").Text);

            // Both environments are on offer in every picker.
            var choices = FormDriver.Field<ComboBox>(form, "_envOriginal").Items.Cast<string>().ToList();
            Assert.Contains("qa", choices);
            Assert.Contains("dev", choices);
        });
    }

    [Fact] // F30
    public void AddToOriginal_ReStampsTheCopyForTheOriginalsEnvironment()
    {
        FormDriver.Run(form =>
        {
            FormDriver.SetText(form, "_pathOriginal", TestEnvironment.FixturePath("qa.tf"));
            FormDriver.Invoke(form, "LoadOriginal", null, EventArgs.Empty);
            FormDriver.SetText(form, "_pathTarget", TestEnvironment.FixturePath("original.tf"));
            FormDriver.Invoke(form, "LoadTarget", null, EventArgs.Empty);

            // Select the dev-only operation in the Target pane and add it.
            var target = FormDriver.Field<BindingList<OperationNode>>(form, "_target");
            var list = FormDriver.Field<ListBox>(form, "_lstTarget");
            list.SelectedIndex = target.IndexOf(target.Single(n => n.OperationId == "get-order-dev"));

            FormDriver.Invoke(form, "AddSelectedToOriginal", "Target");

            var original = FormDriver.Field<BindingList<OperationNode>>(form, "_original");
            var added = original.Single(n => n.UrlTemplate == "orders/{orderId}");
            Assert.Equal("get-order-qa", added.OperationId);
            Assert.Equal("rg-apim-qa", added.ApimResourceGroupName);
            Assert.Equal("apim-company-qa", added.ApimName);
            Assert.Equal("orders-api-qa", added.ApiName);

            // The Target pane still shows its own file's dev operation.
            Assert.Equal("get-order-dev", target.Single(n => n.UrlTemplate == "orders/{orderId}").OperationId);
        });
    }

    [Fact] // F31
    public void SetSelected_MovesTheSelectedOriginalRowsToAnotherEnvironment()
    {
        FormDriver.Run(form =>
        {
            FormDriver.SetText(form, "_pathOriginal", TestEnvironment.FixturePath("original.tf"));
            FormDriver.Invoke(form, "LoadOriginal", null, EventArgs.Empty);

            var list = FormDriver.Field<ListBox>(form, "_lstOriginal");
            list.SelectedIndex = 0;
            FormDriver.Invoke(form, "ApplyEnvironmentToSelection", list, "qa");

            var original = FormDriver.Field<BindingList<OperationNode>>(form, "_original");
            Assert.Equal("qa", original[0].Environment);
            Assert.Equal("list-orders-qa", original[0].OperationId);
            Assert.Equal("dev", original[1].Environment); // unselected row untouched
        });
    }

    [Fact] // F32
    public void RowMenu_RebuildsItsEnvironmentListEveryTimeItOpens()
    {
        FormDriver.Run(form =>
        {
            FormDriver.SetText(form, "_pathOriginal", TestEnvironment.FixturePath("qa.tf"));
            FormDriver.Invoke(form, "LoadOriginal", null, EventArgs.Empty);

            var menu = FormDriver.Field<ListBox>(form, "_lstOriginal").ContextMenuStrip!;
            var onOpening = typeof(ContextMenuStrip)
                .GetMethod("OnOpening", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // Opened twice: the second rebuild disposes the first round's items,
            // which removes them from the collection as it goes.
            onOpening.Invoke(menu, [new CancelEventArgs()]);
            onOpening.Invoke(menu, [new CancelEventArgs()]);

            var environments = (ToolStripMenuItem)menu.Items[1];
            Assert.Equal("Set environment", environments.Text);
            Assert.Contains("qa", environments.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text));
            Assert.Equal(
                environments.DropDownItems.Count,
                environments.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text).Distinct().Count());
        });
    }
}
