using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Models.Hcl;
using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class OperationHclBuilderTests
{
    private static readonly OperationTemplateContext Ctx = new("rg-apim-dev", "apim-company-dev", "my-api-dev");

    [Fact]
    public void Build_ProducesParseableBlockWithContextFields()
    {
        var node = TestOps.Op("post", "users", "createUser", OperationSource.TargetOpenApi,
            "header:authorization", "query:limit");

        var obj = OperationHclBuilder.Build(node, Ctx);

        Assert.Equal("createUser", Literal(obj, "operation_id"));
        Assert.Equal("rg-apim-dev", Literal(obj, "apim_resource_group_name"));
        Assert.Equal("apim-company-dev", Literal(obj, "apim_name"));
        Assert.Equal("my-api-dev", Literal(obj, "api_name"));
        Assert.Equal("POST", Literal(obj, "method"));
        Assert.Equal("users", Literal(obj, "url_template"));

        // The generated object round-trips through the HCL writer/parser.
        var hcl = "op = " + WriteValue(obj);
        var reparsed = new HclParserService().Parse(hcl);
        Assert.NotNull(reparsed);
    }

    [Fact]
    public void Build_EmitsRequestParametersAndResponses()
    {
        var node = new OperationNode
        {
            Source = OperationSource.TargetTerraform,
            Method = "GET",
            UrlTemplate = "users",
            OperationId = "listUsers",
            ParameterKeys = ["header:authorization", "query:limit"],
            ResponseCodes = [200, 404]
        };

        var obj = OperationHclBuilder.Build(node, Ctx);

        var request = Assert.IsType<HclArray>(obj.Get("request"));
        var requestObj = Assert.IsType<HclObject>(request.Items[0].Value);
        Assert.NotNull(requestObj.Get("header"));
        Assert.NotNull(requestObj.Get("query_parameter"));

        var response = Assert.IsType<HclArray>(obj.Get("response"));
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public void Build_EmptyOperationId_UsesPlaceholder()
    {
        var node = TestOps.Op("GET", "users");
        var obj = OperationHclBuilder.Build(node, Ctx);
        Assert.Equal("{operation-id}", Literal(obj, "operation_id"));
    }

    private static string? Literal(HclObject obj, string key) =>
        obj.Get(key) is HclLiteral l ? l.RawValue : null;

    private static string WriteValue(HclObject obj)
    {
        var doc = new HclDocument { RootItems = [new HclAssignment { Key = "op", Value = obj }] };
        var full = new HclWriterService().Write(doc);
        return full["op = ".Length..];
    }
}
