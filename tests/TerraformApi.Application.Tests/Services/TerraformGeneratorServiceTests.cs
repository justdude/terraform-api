using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

public class TerraformGeneratorServiceTests
{
    private readonly TerraformGeneratorService _generator = new();

    private static ApimConfiguration CreateTestConfig() => new()
    {
        ApiGroupName = "test-group",
        Products = [],
        Api = new ApimApi
        {
            ApimResourceGroupName = "rg-apim-dev",
            ApimName = "apim-test",
            Name = "my-api-dev",
            DisplayName = "My API - dev",
            Path = "myapp.dev/v1/api",
            ServiceUrl = "https://api.test.com/v1/service/",
            Protocols = ["https"],
            Revision = "1",
            SoapPassThrough = false,
            SubscriptionRequired = false,
            ProductId = "test-product",
            SubscriptionKeyParameterNames = null,
            Policy = null
        },
        ApiOperations =
        [
            new ApimApiOperation
            {
                OperationId = "get-users-dev",
                ApimResourceGroupName = "rg-apim-dev",
                ApimName = "apim-test",
                ApiName = "my-api-dev",
                DisplayName = "Get Users",
                Method = "GET",
                UrlTemplate = "users",
                StatusCode = 200,
                Description = "",
                Requests =
                [
                    new ApimOperationRequest
                    {
                        Headers =
                        [
                            new ApimParameter
                            {
                                Name = "Authorization",
                                Required = true,
                                Type = "string",
                                Description = "Authorization Header containing Oauth credentials"
                            }
                        ]
                    }
                ]
            },
            new ApimApiOperation
            {
                OperationId = "create-user-dev",
                ApimResourceGroupName = "rg-apim-dev",
                ApimName = "apim-test",
                ApiName = "my-api-dev",
                DisplayName = "Create User",
                Method = "POST",
                UrlTemplate = "users",
                StatusCode = 201,
                Description = "Creates a new user"
            }
        ]
    };

    [Fact]
    public void Generate_ProducesCorrectStructure()
    {
        var config = CreateTestConfig();
        var result = _generator.Generate(config);

        Assert.Contains("test-group = {", result);
        Assert.Contains("product = []", result);
        Assert.Contains("api = [", result);
        Assert.Contains("api_operations = [", result);
    }

    [Fact]
    public void Generate_ApiBlockHasCorrectFields()
    {
        var config = CreateTestConfig();
        var result = _generator.Generate(config);

        Assert.Contains("apim_resource_group_name         = \"rg-apim-dev\"", result);
        Assert.Contains("apim_name                        = \"apim-test\"", result);
        Assert.Contains("name                             = \"my-api-dev\"", result);
        Assert.Contains("display_name                     = \"My API - dev\"", result);
        Assert.Contains("path                             = \"myapp.dev/v1/api\"", result);
        Assert.Contains("service_url                      = \"https://api.test.com/v1/service/\"", result);
        Assert.Contains("protocols                        = [\"https\"]", result);
        Assert.Contains("revision                         = \"1\"", result);
        Assert.Contains("soap_pass_through                = false", result);
        Assert.Contains("subscription_required            = false", result);
        Assert.Contains("product_id                       = \"test-product\"", result);
        Assert.Contains("subscription_key_parameter_names = null", result);
    }

    [Fact]
    public void Generate_OperationsHaveCorrectFields()
    {
        var config = CreateTestConfig();
        var result = _generator.Generate(config);

        Assert.Contains("operation_id             = \"get-users-dev\"", result);
        Assert.Contains("method                   = \"GET\"", result);
        Assert.Contains("url_template             = \"users\"", result);
        Assert.Contains("status_code              = \"200\"", result);

        Assert.Contains("operation_id             = \"create-user-dev\"", result);
        Assert.Contains("method                   = \"POST\"", result);
        Assert.Contains("status_code              = \"201\"", result);
    }

    [Fact]
    public void Generate_RequestHeadersAreIncluded()
    {
        var config = CreateTestConfig();
        var result = _generator.Generate(config);

        Assert.Contains("request = [", result);
        Assert.Contains("header = [", result);
        Assert.Contains("name        = \"Authorization\"", result);
        Assert.Contains("required    = true", result);
        Assert.Contains("type        = \"string\"", result);
    }

    [Fact]
    public void Generate_WithPolicy_IncludesHeredoc()
    {
        var config = CreateTestConfig();
        var configWithPolicy = config with
        {
            Api = config.Api with
            {
                Policy = "<policies>\n  <inbound>\n    <base />\n  </inbound>\n</policies>"
            }
        };

        var result = _generator.Generate(configWithPolicy);

        Assert.Contains("policy = <<XML", result);
        Assert.Contains("<policies>", result);
        Assert.Contains("XML", result);
    }

    [Fact]
    public void Generate_NullProductId_OutputsNull()
    {
        var config = CreateTestConfig() with
        {
            Api = CreateTestConfig().Api with { ProductId = null }
        };

        var result = _generator.Generate(config);

        Assert.Contains("product_id                       = null", result);
    }

    [Fact]
    public void Generate_EmptyOperations_ProducesEmptyBlock()
    {
        var config = CreateTestConfig() with { ApiOperations = [] };
        var result = _generator.Generate(config);

        Assert.Contains("api_operations = [", result);
        Assert.Contains("]", result);
    }

    [Fact]
    public void Generate_WithQueryParameters_IncludesParams()
    {
        var config = new ApimConfiguration
        {
            ApiGroupName = "test",
            Api = CreateTestConfig().Api,
            ApiOperations =
            [
                new ApimApiOperation
                {
                    OperationId = "search-dev",
                    ApimResourceGroupName = "rg",
                    ApimName = "apim",
                    ApiName = "api",
                    DisplayName = "Search",
                    Method = "GET",
                    UrlTemplate = "search",
                    Requests =
                    [
                        new ApimOperationRequest
                        {
                            QueryParameters =
                            [
                                new ApimParameter { Name = "q", Required = true, Type = "string" }
                            ]
                        }
                    ]
                }
            ]
        };

        var result = _generator.Generate(config);
        Assert.Contains("query_parameter = [", result);
        Assert.Contains("name        = \"q\"", result);
    }
}
