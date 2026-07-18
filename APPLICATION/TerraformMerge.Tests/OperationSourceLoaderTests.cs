using TerraformMerge.Engine;

namespace TerraformMerge.Tests;

public class OperationSourceLoaderTests
{
    private readonly OperationSourceLoader _loader = new();

    private const string Terraform = """
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
              request = [
                {
                  header = [
                    { name = "Authorization" },
                  ]
                  query_parameter = [
                    { name = "limit" },
                  ]
                },
              ]
              response = [
                { status_code = 200 },
                { status_code = 400 },
              ]
            },
            {
              operation_id             = "create-user-dev"
              api_name                 = "my-api-dev"
              method                   = "POST"
              url_template             = "users"
              status_code              = "201"
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
              "post": {
                "operationId": "createUser",
                "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } },
                "responses": { "201": { "description": "Created" } }
              }
            }
          }
        }
        """;

    [Fact]
    public void LoadTerraform_ExtractsOperationsWithParamsAndResponses()
    {
        var loaded = _loader.LoadTerraform(Terraform, OperationSource.OriginalTerraform);

        Assert.Equal(2, loaded.Nodes.Count);
        Assert.NotNull(loaded.TerraformDocument);

        var get = loaded.Nodes.Single(n => n.Method == "GET");
        Assert.Equal("users", get.UrlTemplate);
        Assert.Equal("get-users-dev", get.OperationId);
        Assert.Equal("my-group", get.ApiGroupName);
        Assert.Contains("header:authorization", get.ParameterKeys);
        Assert.Contains("query:limit", get.ParameterKeys);
        Assert.Equal([200, 400], get.ResponseCodes);
        Assert.NotNull(get.ArrayItem); // AST preserved for faithful rewrite
    }

    [Fact]
    public void LoadOpenApi_ExtractsOperations()
    {
        var loaded = _loader.LoadOpenApi(OpenApi, OperationSource.TargetOpenApi);

        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Null(loaded.TerraformDocument);

        var post = loaded.Nodes.Single(n => n.Method == "POST");
        Assert.Equal("users", post.UrlTemplate);
        Assert.Equal("createUser", post.OperationId);
        Assert.NotNull(post.OpenApiOperation);
        Assert.Contains("application/json", post.OpenApiOperation!.RequestBodyContentTypes!);
    }

    [Theory]
    [InlineData("""{ "openapi": "3.0.1", "paths": {} }""", true)]
    [InlineData("""{ "swagger": "2.0" }""", true)]
    [InlineData("my-group = { api_operations = [] }", false)]
    [InlineData("not json and not hcl", false)]
    public void LooksLikeOpenApi_Detects(string text, bool expected)
    {
        Assert.Equal(expected, OperationSourceLoader.LooksLikeOpenApi(text));
    }

    [Fact]
    public void Load_AutoDetects()
    {
        var tf = _loader.Load(Terraform, OperationSource.TargetTerraform, OperationSource.TargetOpenApi);
        Assert.True(tf.IsTerraform);

        var api = _loader.Load(OpenApi, OperationSource.TargetTerraform, OperationSource.TargetOpenApi);
        Assert.False(api.IsTerraform);
    }
}
