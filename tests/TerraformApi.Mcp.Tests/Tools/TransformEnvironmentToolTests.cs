using TerraformApi.Application.Services;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>
/// Tests for the TransformEnvironmentTool MCP tool.
/// Uses the real EnvironmentTransformerService (no mocks) to test end-to-end behavior.
/// </summary>
public class TransformEnvironmentToolTests
{
    private readonly IEnvironmentTransformer _transformer = new EnvironmentTransformerService();

    private const string DevTerraform = """
        test-api-group = {
          product = []
          api = [
            {
                apim_resource_group_name         = "rg-apim-dev"
                apim_name                        = "apim-company-dev"
                name                             = "test-api-dev"
                display_name                     = "Test API - dev"
                path                             = "app.dev/v1/api"
                service_url                      = "https://api-dev.company.com/my-service/"
                protocols                        = ["https"]
                revision                         = "1"
                soap_pass_through                = false
                subscription_required            = false
                product_id                       = null
                subscription_key_parameter_names = null
            },
          ]

          api_operations = [
            {
                operation_id             = "get-users-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "GET users"
                method                   = "GET"
                url_template             = "users"
                status_code              = "200"
                description              = ""
            },
            {
                operation_id             = "create-user-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "POST users"
                method                   = "POST"
                url_template             = "users"
                status_code              = "200"
                description              = ""
            },
          ]
        }
        """;

    private const string StagingTerraform = """
        test-api-group = {
          product = []
          api = [
            {
                apim_resource_group_name         = "rg-apim-staging"
                apim_name                        = "apim-company-staging"
                name                             = "test-api-staging"
                display_name                     = "Test API - staging"
                path                             = "app.staging/v1/api"
                service_url                      = "https://api-staging.company.com/my-service/"
                protocols                        = ["https"]
                revision                         = "1"
                soap_pass_through                = false
                subscription_required            = false
                product_id                       = null
                subscription_key_parameter_names = null
            },
          ]

          api_operations = [
            {
                operation_id             = "get-users-staging"
                apim_resource_group_name = "rg-apim-staging"
                apim_name                = "apim-company-staging"
                api_name                 = "test-api-staging"
                display_name             = "GET users"
                method                   = "GET"
                url_template             = "users"
                status_code              = "200"
                description              = ""
            },
            {
                operation_id             = "custom-health-staging"
                apim_resource_group_name = "rg-apim-staging"
                apim_name                = "apim-company-staging"
                api_name                 = "test-api-staging"
                display_name             = "GET health"
                method                   = "GET"
                url_template             = "health"
                status_code              = "200"
                description              = ""
            },
          ]
        }
        """;

    [Fact]
    public void Transform_BasicDevToStaging_ReturnsTerraformWithSummary()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com");

        Assert.Contains("rg-apim-staging", result);
        Assert.Contains("apim-company-staging", result);
        Assert.Contains("api-staging.company.com", result);
        Assert.Contains("test-api-staging", result);
        Assert.Contains("Environment transform: dev -> staging", result);
        Assert.DoesNotContain("Transform failed", result);
    }

    [Fact]
    public void Transform_WithExistingTarget_ShowsSyncSummary()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com",
            existingTargetTerraform: StagingTerraform);

        Assert.Contains("Environment transform: dev -> staging", result);
        Assert.Contains("Synced", result);
        Assert.Contains("Added", result);        // POST /users is new to target
        Assert.Contains("Preserved", result);     // GET /health is target-only
        Assert.DoesNotContain("Transform failed", result);
    }

    [Fact]
    public void Transform_WithExplicitSourceEnv_Works()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com",
            sourceEnvironment: "dev");

        Assert.Contains("rg-apim-staging", result);
        Assert.DoesNotContain("Transform failed", result);
    }

    [Fact]
    public void Transform_EmptySource_ReturnsError()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: "",
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com");

        Assert.Contains("Transform failed", result);
    }

    [Fact]
    public void Transform_UndetectableEnv_ReturnsError()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: """name = "unknown-env-api" """,
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com");

        Assert.Contains("Transform failed", result);
        Assert.Contains("auto-detect", result);
    }

    [Fact]
    public void Transform_SameEnvironment_ReturnsError()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "dev",
            targetStageGroupName: "rg-apim-dev",
            targetApimName: "apim-company-dev",
            targetApiGatewayHost: "api-dev.company.com");

        Assert.Contains("Transform failed", result);
        Assert.Contains("same", result);
    }

    [Fact]
    public void Transform_WithSubscriptionOverride_OverridesValue()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "prod",
            targetStageGroupName: "rg-apim-prod",
            targetApimName: "apim-company-prod",
            targetApiGatewayHost: "api.company.com",
            targetSubscriptionRequired: true);

        Assert.Contains("subscription_required            = true", result);
    }

    [Fact]
    public void Transform_PreservesTargetOnlyOperations_InOutput()
    {
        var result = TransformEnvironmentTool.Transform(
            _transformer,
            sourceTerraform: DevTerraform,
            targetEnvironment: "staging",
            targetStageGroupName: "rg-apim-staging",
            targetApimName: "apim-company-staging",
            targetApiGatewayHost: "api-staging.company.com",
            existingTargetTerraform: StagingTerraform);

        // The custom-health-staging operation should be in the output
        Assert.Contains("custom-health-staging", result);
        Assert.Contains("GET health", result);
    }
}
