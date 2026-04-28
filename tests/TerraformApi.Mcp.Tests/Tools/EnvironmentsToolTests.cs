using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>
/// Tests for the EnvironmentsTool MCP tool.
/// Uses temporary files to simulate appsettings.json in various states,
/// calling the internal ListEnvironmentsFromPath to bypass file-discovery logic.
/// </summary>
public class EnvironmentsToolTests : IDisposable
{
    private readonly string _tempDir;

    public EnvironmentsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-env-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string WriteAppsettings(string json)
    {
        var path = Path.Combine(_tempDir, $"appsettings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ListEnvironments_WithValidConfig_ListsAllEnvironments()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": {
                  "StageGroupName": "rg-apim-dev",
                  "ApimName": "apim-company-dev",
                  "ApiGatewayHost": "api-dev.company.com"
                },
                "prod": {
                  "StageGroupName": "rg-apim-prod",
                  "ApimName": "apim-company-prod",
                  "ApiGatewayHost": "api.company.com"
                }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path);

        Assert.Contains("Available APIM Environment Presets:", result);
        Assert.Contains("[dev]", result);
        Assert.Contains("[prod]", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.Contains("rg-apim-prod", result);
    }

    [Fact]
    public void ListEnvironments_SpecificEnvironment_ReturnsOnlyThatEnvironment()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": {
                  "StageGroupName": "rg-apim-dev",
                  "ApimName": "apim-company-dev"
                },
                "prod": {
                  "StageGroupName": "rg-apim-prod",
                  "ApimName": "apim-company-prod"
                }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path, environmentName: "dev");

        Assert.Contains("Environment: dev", result);
        Assert.Contains("[dev]", result);
        Assert.Contains("rg-apim-dev", result);
        Assert.DoesNotContain("[prod]", result);
    }

    [Fact]
    public void ListEnvironments_NonexistentEnvironment_ReturnsNotFoundWithAvailable()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": { "ApimName": "apim-dev" },
                "staging": { "ApimName": "apim-staging" }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path, environmentName: "prod");

        Assert.Contains("not found", result);
        Assert.Contains("dev", result);
        Assert.Contains("staging", result);
    }

    [Fact]
    public void ListEnvironments_NoApimEnvironmentsSection_ReturnsHelpfulMessage()
    {
        var path = WriteAppsettings("""
            {
              "Logging": { "LogLevel": { "Default": "Warning" } }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path);

        Assert.Contains("No 'ApimEnvironments' section", result);
    }

    [Fact]
    public void ListEnvironments_NullPath_ReturnsNotConfiguredMessage()
    {
        var result = EnvironmentsTool.ListEnvironmentsFromPath(null);

        Assert.Contains("No appsettings.json found", result);
        Assert.Contains("not configured", result);
    }

    [Fact]
    public void ListEnvironments_BooleanValues_FormattedCorrectly()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": {
                  "SubscriptionRequired": false,
                  "IncludeCorsPolicy": true
                }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path, environmentName: "dev");

        Assert.Contains("SubscriptionRequired: false", result);
        Assert.Contains("IncludeCorsPolicy: true", result);
    }

    [Fact]
    public void ListEnvironments_ArrayValues_FormattedAsCommaSeparated()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": {
                  "AllowedMethods": ["GET", "POST", "PUT", "DELETE"]
                }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path, environmentName: "dev");

        Assert.Contains("AllowedMethods: GET, POST, PUT, DELETE", result);
    }

    [Fact]
    public void ListEnvironments_MultipleEnvironments_AllPresent()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": { "ApimName": "apim-dev" },
                "staging": { "ApimName": "apim-staging" },
                "prod": { "ApimName": "apim-prod" }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path);

        Assert.Contains("[dev]", result);
        Assert.Contains("[staging]", result);
        Assert.Contains("[prod]", result);
        Assert.Contains("apim-dev", result);
        Assert.Contains("apim-staging", result);
        Assert.Contains("apim-prod", result);
    }

    [Fact]
    public void ListEnvironments_EmptyEnvironments_ShowsHeaderOnly()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {}
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path);

        Assert.Contains("Available APIM Environment Presets:", result);
        Assert.DoesNotContain("[dev]", result);
    }

    [Fact]
    public void ListEnvironments_InvalidJson_ReturnsErrorMessage()
    {
        var path = Path.Combine(_tempDir, "broken.json");
        File.WriteAllText(path, "{broken json!!");

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path);

        Assert.Contains("Failed to load environment presets:", result);
    }

    [Fact]
    public void ListEnvironments_EnvironmentWithAllFieldTypes_FormatsEachCorrectly()
    {
        var path = WriteAppsettings("""
            {
              "ApimEnvironments": {
                "dev": {
                  "StageGroupName": "rg-apim-dev",
                  "ApimName": "apim-company-dev",
                  "ApiGatewayHost": "api-dev.company.com",
                  "SubscriptionRequired": false,
                  "IncludeCorsPolicy": true,
                  "AllowedMethods": ["GET", "POST"]
                }
              }
            }
            """);

        var result = EnvironmentsTool.ListEnvironmentsFromPath(path, environmentName: "dev");

        Assert.Contains("StageGroupName: rg-apim-dev", result);
        Assert.Contains("ApimName: apim-company-dev", result);
        Assert.Contains("ApiGatewayHost: api-dev.company.com", result);
        Assert.Contains("SubscriptionRequired: false", result);
        Assert.Contains("IncludeCorsPolicy: true", result);
        Assert.Contains("AllowedMethods: GET, POST", result);
    }

    [Fact]
    public void FindAppsettingsPath_ReturnsNonNull_WhenProjectConfigExists()
    {
        // The MCP project's appsettings.json gets copied to the test bin directory,
        // so FindAppsettingsPath should always find it in the test environment.
        var path = EnvironmentsTool.FindAppsettingsPath();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ListEnvironments_ViaPublicApi_ReturnsContent()
    {
        // Test the public API (uses FindAppsettingsPath internally).
        // In the test environment, the MCP project's appsettings.json is in the bin directory.
        var result = EnvironmentsTool.ListEnvironments();

        // Should find the appsettings.json and return some content
        Assert.DoesNotContain("No appsettings.json found", result);
    }
}
