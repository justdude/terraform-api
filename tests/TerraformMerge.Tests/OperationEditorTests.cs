using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// The field editor's application logic: it updates the node, marks it edited,
/// leaves operation_id untouched, and — for Original-sourced nodes — rewrites
/// the underlying HCL surgically so unrelated content survives byte-for-byte.
/// </summary>
public class OperationEditorTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly OriginalWriter _writer = new();

    private static Dictionary<OperationField, string> Edit(params (OperationField, string)[] edits) =>
        edits.ToDictionary(e => e.Item1, e => e.Item2);

    [Fact]
    public void Apply_ChangesFieldAndMarksEdited()
    {
        var node = new OperationNode { Source = OperationSource.TargetOpenApi, Method = "GET", UrlTemplate = "orders" };

        var changed = OperationEditor.Apply(node, Edit((OperationField.UrlTemplate, "orders/{id}")));

        Assert.True(changed);
        Assert.True(node.Edited);
        Assert.Equal("orders/{id}", node.UrlTemplate);
    }

    [Fact]
    public void Apply_MethodIsUpperCased()
    {
        var node = new OperationNode { Source = OperationSource.TargetOpenApi, Method = "GET", UrlTemplate = "orders" };
        OperationEditor.Apply(node, Edit((OperationField.Method, "post")));
        Assert.Equal("POST", node.Method);
    }

    [Fact]
    public void Apply_NoRealChange_ReturnsFalse_DoesNotMarkEdited()
    {
        var node = new OperationNode { Source = OperationSource.TargetOpenApi, Method = "GET", UrlTemplate = "orders" };
        var changed = OperationEditor.Apply(node, Edit((OperationField.Method, "GET")));
        Assert.False(changed);
        Assert.False(node.Edited);
    }

    [Fact]
    public void Apply_UnparsableStatusCode_IsRejected_KeepsPreviousValue()
    {
        // A typo like "20O" must not silently blank an existing status code.
        var node = new OperationNode { Source = OperationSource.TargetOpenApi, Method = "GET", UrlTemplate = "orders", StatusCode = 200 };
        var changed = OperationEditor.Apply(node, Edit((OperationField.StatusCode, "20O")));
        Assert.False(changed);
        Assert.Equal(200, node.StatusCode);
        Assert.DoesNotContain(OperationField.StatusCode, node.EditedFields);
    }

    [Fact]
    public void Apply_BlankStatusCode_ClearsValue()
    {
        var node = new OperationNode { Source = OperationSource.TargetOpenApi, Method = "GET", UrlTemplate = "orders", StatusCode = 200 };
        var changed = OperationEditor.Apply(node, Edit((OperationField.StatusCode, "")));
        Assert.True(changed);
        Assert.Null(node.StatusCode);
    }

    [Fact]
    public void Apply_CaseVariantMethod_IsNoOp()
    {
        // Regression: the no-op check must compare the normalized value, so
        // typing "get" for an existing "GET" is not a spurious edit.
        var node = new OperationNode { Source = OperationSource.TargetTerraform, Method = "GET", UrlTemplate = "orders", ApiName = "orders-api-staging" };
        var changed = OperationEditor.Apply(node, Edit((OperationField.Method, "get")));
        Assert.False(changed);
        Assert.False(node.Edited);
        Assert.Empty(node.EditedFields);
    }

    [Fact]
    public void Apply_ToOriginalNode_RewritesThatFieldSurgically_PreservesEverythingElse()
    {
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");

        // Accept the staging resource group from "the other side".
        OperationEditor.Apply(list, Edit((OperationField.ApimResourceGroupName, "rg-apim-staging")));

        var text = _writer.Rewrite(original, original.Nodes.ToList());

        // The one operation's resource group changed...
        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        var reList = reparsed.Nodes.Single(n => n.OperationId == "list-orders-dev");
        Assert.Equal("rg-apim-staging", reList.ApimResourceGroupName);

        // ...the operation_id was not touched...
        Assert.Equal("list-orders-dev", reList.OperationId);

        // ...its request/response blocks survived (query params + codes intact)...
        Assert.Equal(["query:limit", "query:status"], reList.ParameterKeys);

        // ...the policy heredoc is untouched...
        Assert.Contains("<method>GET</method>", text);

        // ...and the OTHER operations are byte-for-byte unchanged.
        Assert.Contains("apim_resource_group_name = \"rg-apim-dev\"", text); // get-order/create-order still dev
    }

    [Fact]
    public void Apply_ToOriginalNode_InterpolatedValue_WrittenAsInterpolation()
    {
        var original = _loader.LoadTerraform(Sample.Platform, OperationSource.OriginalTerraform);
        var op = original.Nodes.First(n => n.ApiGroupName == "payments-api-group");

        OperationEditor.Apply(op, Edit((OperationField.ApiName, "payments-api-${var.environment}-v2")));

        var text = _writer.Rewrite(original, original.Nodes.ToList());

        // Emitted as a quoted interpolation, not an escaped literal.
        Assert.Contains("api_name                 = \"payments-api-${var.environment}-v2\"", text);
    }

    [Fact]
    public void Apply_OperationIdIsNotAnEditableField()
    {
        // There is no OperationField for the id, so it can never be handed to Apply.
        Assert.DoesNotContain(OperationFieldInfo.All, f => f.HclKey == "operation_id");
    }

    [Fact]
    public void Apply_EditedGeneratedBlock_UsesNodeContextInsteadOfTargetStyle()
    {
        // A target op added to a dev Original would normally blend into dev.
        // After editing its resource group, the generated block must keep the
        // edited value.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var incoming = new OperationNode
        {
            Source = OperationSource.TargetTerraform,
            OperationId = "cancel-order",
            Method = "POST",
            UrlTemplate = "orders/{orderId}/cancel",
            ApiName = "orders-api-staging",
            ApimResourceGroupName = "rg-apim-staging"
        };
        OperationEditor.Apply(incoming, Edit((OperationField.ApimResourceGroupName, "rg-apim-shared")));

        var desired = original.Nodes.Append(incoming).ToList();
        var text = _writer.Rewrite(original, desired);

        Assert.Contains("rg-apim-shared", text);
    }

    [Fact]
    public void Apply_ToOriginalNode_ChangesExactlyOnePhysicalLine()
    {
        // The feature's headline invariant: a scalar edit rewrites only its own
        // line and leaves every other line — heredoc, request/response blocks,
        // comments, alignment — byte-identical.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");

        OperationEditor.Apply(list, Edit((OperationField.ApimResourceGroupName, "rg-apim-staging")));
        var after = _writer.Rewrite(original, original.Nodes.ToList());

        var before = Sample.Apim.Replace("\r\n", "\n").Split('\n');
        var now = after.Replace("\r\n", "\n").Split('\n');

        Assert.Equal(before.Length, now.Length);
        var changedIndices = Enumerable.Range(0, before.Length)
            .Where(i => before[i] != now[i])
            .ToList();

        var i = Assert.Single(changedIndices);
        Assert.Equal("apim_resource_group_name = \"rg-apim-dev\"", before[i].Trim());
        Assert.Equal("apim_resource_group_name = \"rg-apim-staging\"", now[i].Trim());
    }

    [Fact]
    public void Apply_ToOriginalNode_EditedSaveIsAFixedPoint()
    {
        // Edit -> save -> reload -> save must be byte-for-byte stable, so a user
        // who saves twice does not get a churned file.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");
        OperationEditor.Apply(list, Edit((OperationField.ApimResourceGroupName, "rg-apim-staging")));

        var first = _writer.Rewrite(original, original.Nodes.ToList());
        var reloaded = _loader.LoadTerraform(first, OperationSource.OriginalTerraform);
        var second = _writer.Rewrite(reloaded, reloaded.Nodes.ToList());

        Assert.Equal(first, second);
        Assert.Equal("rg-apim-staging",
            reloaded.Nodes.Single(n => n.OperationId == "list-orders-dev").ApimResourceGroupName);
    }

    [Fact]
    public void Apply_ToOriginalNode_MethodNormalizationReachesTheAst()
    {
        // The AST is written from the setter-normalized value, so an edited
        // method reaches the file upper-cased, not as typed.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");

        OperationEditor.Apply(list, Edit((OperationField.Method, "patch")));
        var text = _writer.Rewrite(original, original.Nodes.ToList());

        Assert.Contains("method                   = \"PATCH\"", text);
        Assert.DoesNotContain("\"patch\"", text);
        Assert.Equal("PATCH",
            _loader.LoadTerraform(text, OperationSource.OriginalTerraform)
                   .Nodes.Single(n => n.OperationId == "list-orders-dev").Method);
    }

    [Fact]
    public void Apply_ToOriginalNode_ValueWithQuoteAndBackslash_ProducesParseableHcl()
    {
        // Free-text fields must be HCL-escaped or the saved file is corrupt.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");

        OperationEditor.Apply(list, Edit((OperationField.Description, @"Returns ""all"" orders at C:\data")));
        var text = _writer.Rewrite(original, original.Nodes.ToList());

        // Round-trips: the reparsed description matches what was typed.
        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        var reList = reparsed.Nodes.Single(n => n.OperationId == "list-orders-dev");
        Assert.Equal(@"Returns \""all\"" orders at C:\\data", ReadRawDescription(text));
        Assert.Equal(3, reparsed.Nodes.Count); // still valid: all operations parsed
    }

    [Fact]
    public void Apply_ToOriginalNode_InsertsAbsentFieldBeforeNestedBlocks()
    {
        // Editing a field the operation did not have inserts it above the
        // request/response blocks, not after or inside them.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");
        var op = (TerraformApi.Domain.Models.Hcl.HclObject)list.ArrayItem!.Value;
        op.Items.RemoveAll(i => i is TerraformApi.Domain.Models.Hcl.HclAssignment { Key: "display_name" });

        OperationEditor.Apply(list, Edit((OperationField.DisplayName, "List orders v2")));
        var text = _writer.Rewrite(original, original.Nodes.ToList());

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        var reList = reparsed.Nodes.Single(n => n.OperationId == "list-orders-dev");
        Assert.Equal("List orders v2", reList.DisplayName);
        Assert.Equal(["query:limit", "query:status"], reList.ParameterKeys); // request block survived

        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        var displayLine = lines.FindIndex(l => l.StartsWith("display_name", StringComparison.Ordinal) && l.Contains("List orders v2"));
        var requestLine = lines.FindIndex(l => l.StartsWith("request", StringComparison.Ordinal));
        Assert.True(displayLine >= 0 && requestLine >= 0 && displayLine < requestLine);
    }

    [Fact]
    public void Apply_ToOriginalNode_AbsentFieldWithEmptyValue_AddsNoAssignment()
    {
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "get-order-dev");
        var op = (TerraformApi.Domain.Models.Hcl.HclObject)list.ArrayItem!.Value;
        op.Items.RemoveAll(i => i is TerraformApi.Domain.Models.Hcl.HclAssignment { Key: "display_name" });

        // display_name is absent on the node too after removal? It is still on
        // the node; set it empty and ensure no display_name = "" noise is added.
        list.DisplayName = "";
        var changed = OperationEditor.Apply(list, Edit((OperationField.DisplayName, "")));

        Assert.False(changed);
        var text = _writer.Rewrite(original, original.Nodes.ToList());
        Assert.DoesNotContain("display_name             = \"\"", text);
    }

    [Fact]
    public void Apply_EditingUnrelatedFieldOnAddedNode_DoesNotRepointApiContext()
    {
        // Regression for the per-field edit gate: editing only display_name on a
        // Target op added to a dev Original must NOT drag its staging
        // api/rg/apim into the generated block — those still blend into dev.
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var incoming = new OperationNode
        {
            Source = OperationSource.TargetTerraform,
            OperationId = "delete-order-staging",
            Method = "DELETE",
            UrlTemplate = "orders/{orderId}",
            ApiName = "orders-api-staging",
            ApimResourceGroupName = "rg-apim-staging",
            ApimName = "apim-company-staging"
        };
        OperationEditor.Apply(incoming, Edit((OperationField.DisplayName, "Remove order")));

        var text = _writer.Rewrite(original, original.Nodes.Append(incoming).ToList());
        var reAdded = _loader.LoadTerraform(text, OperationSource.OriginalTerraform)
                             .Nodes.Single(n => n.Method == "DELETE");

        Assert.Equal("orders-api-dev", reAdded.ApiName);
        Assert.Equal("rg-apim-dev", reAdded.ApimResourceGroupName);
        Assert.Equal("apim-company-dev", reAdded.ApimName);
        // None of the staging *context values* leaked (the op id may still say staging).
        Assert.DoesNotContain("orders-api-staging", text);
        Assert.DoesNotContain("rg-apim-staging", text);
        Assert.DoesNotContain("apim-company-staging", text);
    }

    [Fact]
    public void Apply_EditedGeneratedBlock_AllThreeContextFieldsFlowThrough()
    {
        var original = _loader.LoadTerraform(Sample.Apim, OperationSource.OriginalTerraform);
        var incoming = new OperationNode
        {
            Source = OperationSource.TargetTerraform,
            OperationId = "archive-order",
            Method = "POST",
            UrlTemplate = "orders/{orderId}/archive"
        };
        OperationEditor.Apply(incoming, Edit(
            (OperationField.ApiName, "orders-api-shared"),
            (OperationField.ApimName, "apim-company-shared"),
            (OperationField.ApimResourceGroupName, "rg-apim-shared")));

        var text = _writer.Rewrite(original, original.Nodes.Append(incoming).ToList());
        var reAdded = _loader.LoadTerraform(text, OperationSource.OriginalTerraform)
                             .Nodes.Single(n => n.OperationId == "archive-order");

        Assert.Equal("orders-api-shared", reAdded.ApiName);
        Assert.Equal("apim-company-shared", reAdded.ApimName);
        Assert.Equal("rg-apim-shared", reAdded.ApimResourceGroupName);
    }

    [Fact]
    public void Apply_ToOriginalNode_CrlfSource_KeepsUniformCrlf()
    {
        // A CRLF-authored file must stay all-CRLF after an edit re-renders a line.
        var crlf = Sample.Apim.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var original = _loader.LoadTerraform(crlf, OperationSource.OriginalTerraform);
        var list = original.Nodes.Single(n => n.OperationId == "list-orders-dev");

        OperationEditor.Apply(list, Edit((OperationField.ApimResourceGroupName, "rg-apim-staging")));
        var text = _writer.Rewrite(original, original.Nodes.ToList());

        // No bare LF: every "\n" is preceded by "\r".
        Assert.DoesNotContain('\n', text.Replace("\r\n", ""));
        Assert.Contains("rg-apim-staging", text);
    }

    /// <summary>Returns the between-quotes text of list-orders-dev's description line.</summary>
    private static string ReadRawDescription(string text)
    {
        var line = text.Replace("\r\n", "\n").Split('\n')
            .First(l => l.TrimStart().StartsWith("description", StringComparison.Ordinal) && l.Contains("Returns"));
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');
        return line.Substring(first + 1, last - first - 1);
    }

    private static class Sample
    {
        public static string Apim =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-apim.tf"));
        public static string Platform =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "samples", "azure-apim-platform.tf"));
    }
}
