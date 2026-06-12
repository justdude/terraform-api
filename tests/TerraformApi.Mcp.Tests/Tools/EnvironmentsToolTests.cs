using TerraformApi.Domain.Models;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>
/// Tests for the EnvironmentsTool MCP tool.
/// Uses the internal FormatEnvironments method with in-memory dictionaries
/// to test formatting logic without file I/O.
/// </summary>
public class EnvironmentsToolTests
{
    private static Dictionary<string, ApimEnvironmentConfig> CreateDevProdConfig() => new()
    {
        ["dev"] = new ApimEnvironmentConfig
        {
            StageGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            ApiGatewayHost = "api-dev.company.com"
        },
        ["prod"] = new ApimEnvironmentConfig
        {
            StageGroupName = "rg-apim-prod",
            ApimName = "apim-company-prod",
            ApiGatewayHost = "api.company.com"
        }
    };

    [Fact]
    public void FormatEnvironments_WithValidConfig_ListsAllEnvironments()
    {
        var environments = CreateDevProdConfig();
        var result = EnvironmentsTool.FormatEnvironments(environments);

        Assert.Contains("Available APIM Environment Presets:", result);
        Assert.Contains("[dev]", result);
        Assert.Contains("[prod]", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.Contains("rg-apim-prod", result);
    }

    [Fact]
    public void FormatEnvironments_SpecificEnvironment_ReturnsOnlyThatEnvironment()
    {
        var environments = CreateDevProdConfig();
        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "dev");

        Assert.Contains("Environment: dev", result);
        Assert.Contains("[dev]", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.DoesNotContain("[prod]", result);
    }

    [Fact]
    public void FormatEnvironments_NonexistentEnvironment_ReturnsNotFoundWithAvailable()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new() { ApimName = "apim-dev" },
            ["staging"] = new() { ApimName = "apim-staging" }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "prod");

        Assert.Contains("not found", result);
        Assert.Contains("dev", result);
        Assert.Contains("staging", result);
    }

    [Fact]
    public void FormatEnvironments_EmptyDictionary_ReturnsNotConfiguredMessage()
    {
        var result = EnvironmentsTool.FormatEnvironments([]);

        Assert.Contains("No environment presets configured", result);
    }

    [Fact]
    public void FormatEnvironments_BooleanValues_FormattedCorrectly()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new()
            {
                SubscriptionRequired = false,
                IncludeCorsPolicy = true
            }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "dev");

        Assert.Contains("SubscriptionRequired: false", result);
        Assert.Contains("IncludeCorsPolicy: true", result);
    }

    [Fact]
    public void FormatEnvironments_ArrayValues_FormattedAsCommaSeparated()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new()
            {
                AllowedMethods = ["GET", "POST", "PUT", "DELETE"]
            }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "dev");

        Assert.Contains("AllowedMethods: GET, POST, PUT, DELETE", result);
    }

    [Fact]
    public void FormatEnvironments_MultipleEnvironments_AllPresent()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new() { ApimName = "apim-dev" },
            ["staging"] = new() { ApimName = "apim-staging" },
            ["prod"] = new() { ApimName = "apim-prod" }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments);

        Assert.Contains("[dev]", result);
        Assert.Contains("[staging]", result);
        Assert.Contains("[prod]", result);
        Assert.Contains("apim-dev", result);
        Assert.Contains("apim-staging", result);
        Assert.Contains("apim-prod", result);
    }

    [Fact]
    public void FormatEnvironments_EnvironmentWithAllFieldTypes_FormatsEachCorrectly()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new()
            {
                StageGroupName = "rg-apim-dev",
                ApimName = "apim-company-dev",
                ApiGatewayHost = "api-dev.company.com",
                FrontendHost = "portal",
                CompanyDomain = "company.com",
                LocalDevHost = "localhost",
                LocalDevPort = "3000",
                SubscriptionRequired = false,
                IncludeCorsPolicy = true,
                AllowedMethods = ["GET", "POST"]
            }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "dev");

        Assert.Contains("StageGroupName: rg-apim-dev", result);
        Assert.Contains("ApimName: apim-company-dev", result);
        Assert.Contains("ApiGatewayHost: api-dev.company.com", result);
        Assert.Contains("FrontendHost: portal", result);
        Assert.Contains("CompanyDomain: company.com", result);
        Assert.Contains("LocalDevHost: localhost", result);
        Assert.Contains("LocalDevPort: 3000", result);
        Assert.Contains("SubscriptionRequired: false", result);
        Assert.Contains("IncludeCorsPolicy: true", result);
        Assert.Contains("AllowedMethods: GET, POST", result);
    }

    [Fact]
    public void FormatEnvironments_NullOptionalFields_OmitsThemFromOutput()
    {
        var environments = new Dictionary<string, ApimEnvironmentConfig>
        {
            ["dev"] = new()
            {
                ApimName = "apim-dev"
                // All other fields are null/default
            }
        };

        var result = EnvironmentsTool.FormatEnvironments(environments, environmentName: "dev");

        Assert.Contains("ApimName: apim-dev", result);
        Assert.DoesNotContain("FrontendHost:", result);
        Assert.DoesNotContain("CompanyDomain:", result);
        Assert.DoesNotContain("LocalDevHost:", result);
        Assert.DoesNotContain("LocalDevPort:", result);
    }
}
