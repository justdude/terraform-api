using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class OriginalWriterTests
{
    private readonly OperationSourceLoader _loader = new();
    private readonly OriginalWriter _writer = new();
    private readonly MergeEngine _merge = new();

    private const string Original = """
        my-group = {
          product = []
          api = [
            {
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              name                     = "my-api-dev"
            },
          ]
          api_operations = [
            {
              operation_id             = "get-users-dev"
              apim_resource_group_name = "rg-apim-dev"
              apim_name                = "apim-company-dev"
              api_name                 = "my-api-dev"
              display_name             = "Get users"
              method                   = "GET"
              url_template             = "users"
              status_code              = "200"
              description              = ""
            },
          ]
        }
        """;

    private const string OpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "User API", "version": "1.0.0" },
          "paths": {
            "/users": {
              "get": { "operationId": "listUsers", "responses": { "200": { "description": "OK" } } },
              "post": { "operationId": "createUser", "responses": { "201": { "description": "Created" } } }
            }
          }
        }
        """;

    [Fact]
    public void Rewrite_KeepingOnlyOriginal_ReturnsUnchangedText()
    {
        var original = _loader.LoadTerraform(Original, OperationSource.OriginalTerraform);

        var text = _writer.Rewrite(original, original.Nodes.ToList());

        Assert.Equal(Original, text);
    }

    [Fact]
    public void Rewrite_AppendingOpenApiOperation_AddsGeneratedBlock_KeepsOriginalVerbatim()
    {
        var original = _loader.LoadTerraform(Original, OperationSource.OriginalTerraform);
        var target = _loader.LoadOpenApi(OpenApi, OperationSource.TargetOpenApi);

        var desired = _merge.AppendMissing(original.Nodes, target.Nodes);
        var text = _writer.Rewrite(original, desired);

        // The original GET block is reproduced byte-for-byte.
        Assert.Contains("operation_id             = \"get-users-dev\"", text);
        // The POST /users operation was appended.
        Assert.Contains("createUser", text);

        // Output re-parses to exactly two operations.
        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal(2, reparsed.Nodes.Count);
        Assert.Contains(reparsed.Nodes, n => n.Method == "POST" && n.UrlTemplate == "users");
    }

    [Fact]
    public void Rewrite_RemovingAll_ProducesEmptyArrayThatReparses()
    {
        var original = _loader.LoadTerraform(Original, OperationSource.OriginalTerraform);

        var text = _writer.Rewrite(original, []);

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Empty(reparsed.Nodes);
    }

    [Fact]
    public void Rewrite_ReorderingOriginals_RespectsNewOrder()
    {
        const string twoOps = """
            g = {
              api_operations = [
                {
                  operation_id = "a-dev"
                  method       = "GET"
                  url_template = "a"
                },
                {
                  operation_id = "b-dev"
                  method       = "POST"
                  url_template = "b"
                },
              ]
            }
            """;

        var original = _loader.LoadTerraform(twoOps, OperationSource.OriginalTerraform);
        var reversed = original.Nodes.Reverse().ToList();

        var text = _writer.Rewrite(original, reversed);

        var reparsed = _loader.LoadTerraform(text, OperationSource.OriginalTerraform);
        Assert.Equal("b-dev", reparsed.Nodes[0].OperationId);
        Assert.Equal("a-dev", reparsed.Nodes[1].OperationId);
    }

    [Fact]
    public void Rewrite_IsIdempotent_WhenAppliedTwice()
    {
        var original = _loader.LoadTerraform(Original, OperationSource.OriginalTerraform);
        var target = _loader.LoadOpenApi(OpenApi, OperationSource.TargetOpenApi);
        var desired = _merge.AppendMissing(original.Nodes, target.Nodes);

        var first = _writer.Rewrite(original, desired);

        // Reload the produced text and merge the same target again — nothing new to add.
        var reloaded = _loader.LoadTerraform(first, OperationSource.OriginalTerraform);
        var secondDesired = _merge.AppendMissing(reloaded.Nodes, target.Nodes);
        var second = _writer.Rewrite(reloaded, secondDesired);

        Assert.Equal(
            _loader.LoadTerraform(first, OperationSource.OriginalTerraform).Nodes.Count,
            _loader.LoadTerraform(second, OperationSource.OriginalTerraform).Nodes.Count);
    }
}
