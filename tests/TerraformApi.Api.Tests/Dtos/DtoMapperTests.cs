using TerraformApi.Api.Dtos;
using TerraformApi.Domain.Models;

namespace TerraformApi.Api.Tests.Dtos;

/// <summary>
/// Tests for the DTO mapper that converts between API request/response DTOs
/// and the domain model types used by the application services.
/// </summary>
public class DtoMapperTests
{
    [Fact]
    public void ToSettings_MapsAllRequiredFields()
    {
        var request = new ConvertRequest
        {
            OpenApiJson = "{}",
            Environment = "staging",
            ApiGroupName = "my-group",
            StageGroupName = "rg-staging",
            ApimName = "apim-staging",
            ApiPathPrefix = "myapp",
            ApiPathSuffix = "api",
            ApiGatewayHost = "gw.company.com",
            BackendServicePath = "backend"
        };

        var settings = DtoMapper.ToSettings(request);

        Assert.Equal("staging", settings.Environment);
        Assert.Equal("my-group", settings.ApiGroupName);
        Assert.Equal("rg-staging", settings.StageGroupName);
        Assert.Equal("apim-staging", settings.ApimName);
        Assert.Equal("myapp", settings.ApiPathPrefix);
        Assert.Equal("api", settings.ApiPathSuffix);
        Assert.Equal("gw.company.com", settings.ApiGatewayHost);
        Assert.Equal("backend", settings.BackendServicePath);
    }

    [Fact]
    public void ToSettings_MapsOptionalFields()
    {
        var request = new ConvertRequest
        {
            OpenApiJson = "{}",
            Environment = "dev",
            ApiGroupName = "g",
            StageGroupName = "rg",
            ApimName = "apim",
            ApiPathPrefix = "p",
            ApiPathSuffix = "s",
            ApiGatewayHost = "gw",
            BackendServicePath = "b",
            ApiName = "custom-name",
            ApiVersion = "v2",
            Revision = "3",
            ProductId = "prod-1",
            FrontendHost = "portal",
            CompanyDomain = "example.com",
            LocalDevHost = "127.0.0.1",
            LocalDevPort = "4200",
            OperationPrefix = "op",
            IncludeCorsPolicy = false,
            SubscriptionRequired = true
        };

        var settings = DtoMapper.ToSettings(request);

        Assert.Equal("custom-name", settings.ApiName);
        Assert.Equal("v2", settings.ApiVersion);
        Assert.Equal("3", settings.Revision);
        Assert.Equal("prod-1", settings.ProductId);
        Assert.Equal("portal", settings.FrontendHost);
        Assert.Equal("example.com", settings.CompanyDomain);
        Assert.Equal("127.0.0.1", settings.LocalDevHost);
        Assert.Equal("4200", settings.LocalDevPort);
        Assert.Equal("op", settings.OperationPrefix);
        Assert.False(settings.IncludeCorsPolicy);
        Assert.True(settings.SubscriptionRequired);
    }

    [Fact]
    public void ToSettings_DefaultApiVersion_IsV1()
    {
        var request = new ConvertRequest
        {
            OpenApiJson = "{}",
            Environment = "dev",
            ApiGroupName = "g",
            StageGroupName = "rg",
            ApimName = "apim",
            ApiPathPrefix = "p",
            ApiPathSuffix = "s",
            ApiGatewayHost = "gw",
            BackendServicePath = "b"
        };

        var settings = DtoMapper.ToSettings(request);

        Assert.Equal("v1", settings.ApiVersion);
        Assert.Equal("1", settings.Revision);
    }

    [Fact]
    public void ToResponse_SuccessfulResult_MapsAllFields()
    {
        var result = new ConversionResult
        {
            Success = true,
            TerraformConfig = "my-group = { }",
            Warnings = ["warn1"],
            Errors = [],
            Configuration = new ApimConfiguration
            {
                ApiGroupName = "my-group",
                Api = new ApimApi
                {
                    ApimResourceGroupName = "rg",
                    ApimName = "apim",
                    Name = "api-dev",
                    DisplayName = "API - dev",
                    Path = "app.dev/v1/api",
                    ServiceUrl = "https://gw/v1/svc/"
                },
                ApiOperations =
                [
                    new ApimApiOperation
                    {
                        OperationId = "get-items-dev",
                        ApimResourceGroupName = "rg",
                        ApimName = "apim",
                        ApiName = "api-dev",
                        DisplayName = "Get Items",
                        Method = "GET",
                        UrlTemplate = "items"
                    }
                ]
            }
        };

        var response = DtoMapper.ToResponse(result);

        Assert.True(response.Success);
        Assert.Equal("my-group = { }", response.TerraformConfig);
        Assert.Single(response.Warnings);
        Assert.NotNull(response.Summary);
        Assert.Equal("api-dev", response.Summary.ApiName);
        Assert.Equal("API - dev", response.Summary.DisplayName);
        Assert.Equal("app.dev/v1/api", response.Summary.Path);
        Assert.Equal(1, response.Summary.OperationCount);
        Assert.Single(response.Summary.Operations);
        Assert.Equal("GET", response.Summary.Operations[0].Method);
        Assert.Equal("items", response.Summary.Operations[0].UrlTemplate);
        Assert.Equal("get-items-dev", response.Summary.Operations[0].OperationId);
    }

    [Fact]
    public void ToResponse_FailedResult_HasNoSummary()
    {
        var result = new ConversionResult
        {
            Success = false,
            Errors = ["something broke", "another error"]
        };

        var response = DtoMapper.ToResponse(result);

        Assert.False(response.Success);
        Assert.Equal(2, response.Errors.Count);
        Assert.Null(response.Summary);
        Assert.Empty(response.TerraformConfig);
    }

    [Fact]
    public void ToResponse_NoConfiguration_HasNoSummary()
    {
        var result = new ConversionResult
        {
            Success = true,
            TerraformConfig = "config"
        };

        var response = DtoMapper.ToResponse(result);

        Assert.True(response.Success);
        Assert.Null(response.Summary);
    }

    [Fact]
    public void ToSettings_AllowedMethodsPreserved()
    {
        var request = new ConvertRequest
        {
            OpenApiJson = "{}",
            Environment = "dev",
            ApiGroupName = "g",
            StageGroupName = "rg",
            ApimName = "apim",
            ApiPathPrefix = "p",
            ApiPathSuffix = "s",
            ApiGatewayHost = "gw",
            BackendServicePath = "b",
            AllowedMethods = ["GET", "POST"]
        };

        var settings = DtoMapper.ToSettings(request);

        Assert.Equal(2, settings.AllowedMethods.Count);
        Assert.Contains("GET", settings.AllowedMethods);
        Assert.Contains("POST", settings.AllowedMethods);
    }
}
