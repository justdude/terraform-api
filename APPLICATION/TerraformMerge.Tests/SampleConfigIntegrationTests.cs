using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

/// <summary>
/// End-to-end tests over the realistic fixtures: a nested
/// (apis.bpc_apis.backend_apis) APIM Terraform config with a CORS policy
/// heredoc and request/response blocks, plus a matching OpenAPI document.
/// </summary>
public class SampleConfigIntegrationTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly MergeEngine _merge = new();
    private readonly OriginalWriter _writer = new();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private LoadedOperations LoadApim() =>
        _loader.LoadTerraform(Fixture("sample-apim.tf"), OperationSource.OriginalTerraform);

    private LoadedOperations LoadOpenApi() =>
        _loader.LoadOpenApi(Fixture("sample-openapi.json"), OperationSource.TargetOpenApi);

    [Fact]
    public void LoadTerraform_NestedStructure_ThreeOperations()
    {
        var loaded = LoadApim();

        Assert.Equal(3, loaded.Nodes.Count);
        Assert.All(loaded.Nodes, n => Assert.Equal("orders-api-group", n.ApiGroupName));
        Assert.Equal(["apis", "bpc_apis", "backend_apis"], loaded.TerraformDocument!.ApiGroupParentPath);
    }

    [Fact]
    public void LoadTerraform_PolicyHeredocMethodTags_NotParsedAsOperations()
    {
        // The <method>GET</method> etc. inside the CORS policy heredoc must never
        // leak into the operations list — only the three real api_operations.
        var loaded = LoadApim();

        var methods = loaded.Nodes.Select(n => n.Method).OrderBy(m => m).ToList();
        Assert.Equal(["GET", "GET", "POST"], methods);
        Assert.Equal(["create-order-dev", "get-order-dev", "list-orders-dev"],
            loaded.Nodes.Select(n => n.OperationId).OrderBy(x => x).ToList());
    }

    [Fact]
    public void LoadTerraform_ExtractsParametersAndResponses()
    {
        var loaded = LoadApim();

        var list = loaded.Nodes.Single(n => n.OperationId == "list-orders-dev");
        Assert.Equal(["query:limit", "query:status"], list.ParameterKeys);

        var create = loaded.Nodes.Single(n => n.OperationId == "create-order-dev");
        Assert.Contains("header:authorization", create.ParameterKeys);

        var get = loaded.Nodes.Single(n => n.OperationId == "get-order-dev");
        Assert.Equal([200, 404], get.ResponseCodes);
    }

    [Fact]
    public void Rewrite_Unchanged_IsByteForByteIdentical()
    {
        var loaded = LoadApim();
        var text = _writer.Rewrite(loaded, loaded.Nodes.ToList());
        Assert.Equal(Fixture("sample-apim.tf"), text);
    }

    [Fact]
    public void ApiMode_ComputeDiff_TwoAddedTwoChanged()
    {
        var original = LoadApim();
        var target = LoadOpenApi();

        var diff = _merge.ComputeDiff(original.Nodes, target.Nodes);

        var added = diff.Where(d => d.Kind == DiffKind.Added).ToList();
        Assert.Equal(2, added.Count);
        Assert.Contains(added, d => d.Operation.Method == "PUT" && d.Operation.UrlTemplate == "orders/{orderId}");
        Assert.Contains(added, d => d.Operation.Method == "DELETE" && d.Operation.UrlTemplate == "orders/{orderId}");

        // getOrder (adds a path param) and createOrder (drops the auth header) match but changed.
        Assert.Contains(diff, d => d.Kind == DiffKind.Changed);
    }

    [Fact]
    public void ApiMode_Merge_AddsTwo_PreservesOriginalsVerbatim_KeepsNesting()
    {
        var original = LoadApim();
        var target = LoadOpenApi();

        var desired = _merge.AppendMissing(original.Nodes, target.Nodes);
        Assert.Equal(5, desired.Count);

        var text = _writer.Rewrite(original, desired);

        // Original operations reproduced byte-for-byte, including the policy heredoc.
        Assert.Contains("operation_id             = \"list-orders-dev\"", text);
        Assert.Contains("<method>GET</method>", text);

        // The two new operations were appended and generated in the file's style.
        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(5, reparsed.Nodes.Count);
        Assert.Contains(reparsed.Nodes, n => n.Method == "PUT" && n.UrlTemplate == "orders/{orderId}");
        Assert.Contains(reparsed.Nodes, n => n.Method == "DELETE" && n.UrlTemplate == "orders/{orderId}");

        // The nested apis.bpc_apis.backend_apis structure survives the rewrite.
        Assert.Equal(["apis", "bpc_apis", "backend_apis"], reparsed.TerraformDocument!.ApiGroupParentPath);
    }

    [Fact]
    public void ApiMode_Merge_IsConvergent_SecondPassAddsNothing()
    {
        var original = LoadApim();
        var target = LoadOpenApi();

        var first = _writer.Rewrite(original, _merge.AppendMissing(original.Nodes, target.Nodes));

        var reloaded = _loader.LoadTerraform(first, OperationSource.OriginalTerraform);
        var secondDesired = _merge.AppendMissing(reloaded.Nodes, target.Nodes);

        Assert.Equal(reloaded.Nodes.Count, secondDesired.Count); // nothing new to add
    }
}
