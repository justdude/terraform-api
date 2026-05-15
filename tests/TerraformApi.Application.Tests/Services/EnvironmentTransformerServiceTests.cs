using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Tests for the EnvironmentTransformerService covering:
/// - Environment auto-detection from Terraform field values
/// - Field value extraction and host parsing
/// - Environment pattern replacement in identifiers and paths
/// - Full transform (source-only and with existing target merge)
/// - Operation matching by url_template + method
/// - Edge cases and error handling
/// </summary>
public class EnvironmentTransformerServiceTests
{
    private readonly EnvironmentTransformerService _transformer = new();

    /// <summary>
    /// Builds a realistic Terraform HCL block for an APIM API configuration.
    /// </summary>
    private static string BuildTerraform(
        string env,
        string rgName,
        string apimName,
        string gatewayHost,
        params (string opId, string method, string url)[] operations)
    {
        var ops = string.Join("\n", operations.Select(o => $$"""
            {
                operation_id             = "{{o.opId}}"
                apim_resource_group_name = "{{rgName}}"
                apim_name                = "{{apimName}}"
                api_name                 = "test-api-{{env}}"
                display_name             = "{{o.method}} {{o.url}}"
                method                   = "{{o.method}}"
                url_template             = "{{o.url}}"
                status_code              = "200"
                description              = ""
            },
        """));

        return $$"""
            test-api-group = {
              product = []
              api = [
                {
                    apim_resource_group_name         = "{{rgName}}"
                    apim_name                        = "{{apimName}}"
                    name                             = "test-api-{{env}}"
                    display_name                     = "Test API - {{env}}"
                    path                             = "app.{{env}}/v1/api"
                    service_url                      = "https://{{gatewayHost}}/my-service/"
                    protocols                        = ["https"]
                    revision                         = "1"
                    soap_pass_through                = false
                    subscription_required            = false
                    product_id                       = null
                    subscription_key_parameter_names = null
                },
              ]

              api_operations = [
            {{ops}}
              ]
            }
            """;
    }

    private static EnvironmentTransformSettings StagingSettings => new()
    {
        TargetEnvironment = "staging",
        TargetStageGroupName = "rg-apim-staging",
        TargetApimName = "apim-company-staging",
        TargetApiGatewayHost = "api-staging.company.com"
    };

    private static EnvironmentTransformSettings ProdSettings => new()
    {
        TargetEnvironment = "prod",
        TargetStageGroupName = "rg-apim-prod",
        TargetApimName = "apim-company-prod",
        TargetApiGatewayHost = "api.company.com"
    };

    // ── Environment Detection ────────────────────────────────────────

    [Fact]
    public void DetectSourceEnvironment_FromResourceGroup_ReturnsDev()
    {
        var terraform = """apim_resource_group_name = "rg-apim-dev" """;
        Assert.Equal("dev", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_FromApimName_ReturnsStaging()
    {
        var terraform = """apim_name = "apim-company-staging" """;
        Assert.Equal("staging", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_FromPath_ReturnsProd()
    {
        var terraform = """path = "myapp.prod/v1/api" """;
        Assert.Equal("prod", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_FromOperationId_ReturnsDev()
    {
        var terraform = """operation_id = "get-users-dev" """;
        Assert.Equal("dev", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_FromName_ReturnsUat()
    {
        var terraform = """name = "my-api-uat" """;
        Assert.Equal("uat", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_NoKnownEnv_ReturnsNull()
    {
        var terraform = """name = "my-api-custom" """;
        Assert.Null(EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_DoesNotMatchSubstring_Developer()
    {
        // "developer" contains "dev" but should NOT be detected as "dev" environment
        // because "dev" must appear as a distinct segment (after a separator)
        var terraform = """name = "developer-portal" """;
        Assert.Null(EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    [Fact]
    public void DetectSourceEnvironment_UnderscoreSeparator_ReturnsDev()
    {
        var terraform = """name = "my_api_dev" """;
        Assert.Equal("dev", EnvironmentTransformerService.DetectSourceEnvironment(terraform));
    }

    // ── Field Extraction ─────────────────────────────────────────────

    [Fact]
    public void ExtractFirstFieldValue_StandardField_ReturnsValue()
    {
        var terraform = """apim_name = "apim-company-dev" """;
        Assert.Equal("apim-company-dev", EnvironmentTransformerService.ExtractFirstFieldValue(terraform, "apim_name"));
    }

    [Fact]
    public void ExtractFirstFieldValue_AlignedWhitespace_ReturnsValue()
    {
        var terraform = """apim_name                        = "apim-company-dev" """;
        Assert.Equal("apim-company-dev", EnvironmentTransformerService.ExtractFirstFieldValue(terraform, "apim_name"));
    }

    [Fact]
    public void ExtractFirstFieldValue_MissingField_ReturnsNull()
    {
        var terraform = """apim_name = "value" """;
        Assert.Null(EnvironmentTransformerService.ExtractFirstFieldValue(terraform, "nonexistent"));
    }

    [Fact]
    public void ExtractHostFromUrl_ValidUrl_ReturnsHost()
    {
        Assert.Equal("api-dev.company.com",
            EnvironmentTransformerService.ExtractHostFromUrl("https://api-dev.company.com/my-service/"));
    }

    [Fact]
    public void ExtractHostFromUrl_UrlWithPort_ReturnsHost()
    {
        Assert.Equal("localhost",
            EnvironmentTransformerService.ExtractHostFromUrl("http://localhost:3000/api"));
    }

    // ── Environment Pattern Replacement ──────────────────────────────

    [Fact]
    public void ReplaceEnvironmentPatterns_HyphenSuffix_Replaces()
    {
        var input = """name = "test-api-dev" """;
        var result = EnvironmentTransformerService.ReplaceEnvironmentPatterns(input, "dev", "staging");
        Assert.Contains("test-api-staging", result);
    }

    [Fact]
    public void ReplaceEnvironmentPatterns_DotSegment_Replaces()
    {
        var input = """path = "myapp.dev/v1/api" """;
        var result = EnvironmentTransformerService.ReplaceEnvironmentPatterns(input, "dev", "staging");
        Assert.Contains("myapp.staging/v1/api", result);
    }

    [Fact]
    public void ReplaceEnvironmentPatterns_DisplayNameSuffix_Replaces()
    {
        var input = """display_name = "My API - dev" """;
        var result = EnvironmentTransformerService.ReplaceEnvironmentPatterns(input, "dev", "staging");
        Assert.Contains("My API - staging", result);
    }

    [Fact]
    public void ReplaceEnvironmentPatterns_UnderscoreSuffix_Replaces()
    {
        var input = """name = "my_api_dev" """;
        var result = EnvironmentTransformerService.ReplaceEnvironmentPatterns(input, "dev", "staging");
        Assert.Contains("my_api_staging", result);
    }

    [Fact]
    public void ReplaceEnvironmentPatterns_DoesNotReplaceSubstring()
    {
        // "developer" should NOT have "dev" replaced
        var input = """description = "For developers only" """;
        var result = EnvironmentTransformerService.ReplaceEnvironmentPatterns(input, "dev", "staging");
        Assert.Contains("developers", result);
        Assert.DoesNotContain("stagingelopers", result);
    }

    [Fact]
    public void ReplaceFieldBoolValue_FalseToTrue_Replaces()
    {
        var input = """subscription_required = false""";
        var result = EnvironmentTransformerService.ReplaceFieldBoolValue(input, "subscription_required", true);
        Assert.Contains("subscription_required = true", result);
    }

    [Fact]
    public void ReplaceFieldBoolValue_TrueToFalse_Replaces()
    {
        var input = """subscription_required = true""";
        var result = EnvironmentTransformerService.ReplaceFieldBoolValue(input, "subscription_required", false);
        Assert.Contains("subscription_required = false", result);
    }

    // ── Operation Extraction ─────────────────────────────────────────

    [Fact]
    public void ExtractOperationBlocks_SingleOperation_ReturnsOne()
    {
        var terraform = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var blocks = EnvironmentTransformerService.ExtractOperationBlocks(terraform);

        Assert.Single(blocks);
        Assert.Contains("get-users-dev", blocks[0]);
    }

    [Fact]
    public void ExtractOperationBlocks_MultipleOperations_ReturnsAll()
    {
        var terraform = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"),
            ("delete-user-dev", "DELETE", "users/{id}"));

        var blocks = EnvironmentTransformerService.ExtractOperationBlocks(terraform);

        Assert.Equal(3, blocks.Count);
    }

    [Fact]
    public void ExtractOperationBlocks_BracesInsideQuotedStrings_IgnoredCorrectly()
    {
        // Braces inside JSON policy strings should not affect block boundary detection
        var terraform = """
            api_operations = [
              {
                  operation_id = "get-users-dev"
                  method       = "GET"
                  url_template = "users"
                  description  = "Returns {\"items\": []}"
              },
              {
                  operation_id = "create-user-dev"
                  method       = "POST"
                  url_template = "users"
              },
            ]
            """;

        var blocks = EnvironmentTransformerService.ExtractOperationBlocks(terraform);

        Assert.Equal(2, blocks.Count);
        Assert.Contains("get-users-dev", blocks[0]);
        Assert.Contains("create-user-dev", blocks[1]);
    }

    [Fact]
    public void ExtractOperationsByRoute_ReturnsCorrectKeys()
    {
        var terraform = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"));

        var ops = EnvironmentTransformerService.ExtractOperationsByRoute(terraform);

        Assert.Equal(2, ops.Count);
        Assert.True(ops.ContainsKey("GET users"));
        Assert.True(ops.ContainsKey("POST users"));
    }

    [Fact]
    public void ExtractOperationsByRoute_CaseInsensitiveKeys()
    {
        var terraform = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var ops = EnvironmentTransformerService.ExtractOperationsByRoute(terraform);

        Assert.True(ops.ContainsKey("get users"));
        Assert.True(ops.ContainsKey("GET users"));
    }

    // ── Full Transform (Source Only) ─────────────────────────────────

    [Fact]
    public void Transform_BasicDevToStaging_TransformsAllFields()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var result = _transformer.Transform(source, StagingSettings);

        Assert.True(result.Success);
        Assert.Equal("dev", result.DetectedSourceEnvironment);

        // Check all fields were transformed
        Assert.Contains("rg-apim-staging", result.TransformedTerraform);
        Assert.Contains("apim-company-staging", result.TransformedTerraform);
        Assert.Contains("api-staging.company.com", result.TransformedTerraform);
        Assert.Contains("test-api-staging", result.TransformedTerraform);
        Assert.Contains("Test API - staging", result.TransformedTerraform);
        Assert.Contains("app.staging/v1/api", result.TransformedTerraform);
        Assert.Contains("get-users-staging", result.TransformedTerraform);

        // Check source fields are gone
        Assert.DoesNotContain("rg-apim-dev", result.TransformedTerraform);
        Assert.DoesNotContain("apim-company-dev", result.TransformedTerraform);
        Assert.DoesNotContain("api-dev.company.com", result.TransformedTerraform);
    }

    [Fact]
    public void Transform_DevToProd_TransformsCorrectly()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"));

        var result = _transformer.Transform(source, ProdSettings);

        Assert.True(result.Success);
        Assert.Contains("rg-apim-prod", result.TransformedTerraform);
        Assert.Contains("apim-company-prod", result.TransformedTerraform);
        Assert.Contains("api.company.com", result.TransformedTerraform);
        Assert.Contains("get-users-prod", result.TransformedTerraform);
        Assert.Contains("create-user-prod", result.TransformedTerraform);
        Assert.Contains("test-api-prod", result.TransformedTerraform);
    }

    [Fact]
    public void Transform_ExplicitSourceEnv_SkipsDetection()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var settings = StagingSettings with { SourceEnvironment = "dev" };
        var result = _transformer.Transform(source, settings);

        Assert.True(result.Success);
        Assert.Equal("dev", result.DetectedSourceEnvironment);
    }

    [Fact]
    public void Transform_WithSubscriptionOverride_ReplacesValue()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var settings = StagingSettings with { TargetSubscriptionRequired = true };
        var result = _transformer.Transform(source, settings);

        Assert.True(result.Success);
        Assert.Contains("subscription_required            = true", result.TransformedTerraform);
    }

    [Fact]
    public void Transform_SourceOnly_ReportsSummary()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"));

        var result = _transformer.Transform(source, StagingSettings);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary.TotalOperations);
        Assert.Equal(2, result.Summary.AddedOperations.Count);
        Assert.Empty(result.Summary.SyncedOperations);
        Assert.Empty(result.Summary.PreservedOperations);
    }

    [Fact]
    public void Transform_PreservesFormatting_IndentationKept()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var result = _transformer.Transform(source, StagingSettings);

        Assert.True(result.Success);
        // Verify indentation is preserved (HCL alignment padding)
        Assert.Contains("apim_resource_group_name         = \"rg-apim-staging\"", result.TransformedTerraform);
        Assert.Contains("apim_name                        = \"apim-company-staging\"", result.TransformedTerraform);
    }

    // ── Full Transform + Merge ───────────────────────────────────────

    [Fact]
    public void Transform_WithExistingTarget_SyncsMatchingOperations()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"));

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"),
            ("create-user-staging", "POST", "users"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary.SyncedOperations.Count);
        Assert.Empty(result.Summary.AddedOperations);
        Assert.Empty(result.Summary.PreservedOperations);
    }

    [Fact]
    public void Transform_WithExistingTarget_AddsNewOperations()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"),
            ("delete-user-dev", "DELETE", "users/{id}")); // new operation not in target

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"),
            ("create-user-staging", "POST", "users"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary.SyncedOperations.Count);
        Assert.Single(result.Summary.AddedOperations);
        Assert.Contains("DELETE users/{id}", result.Summary.AddedOperations);
        Assert.Contains("delete-user-staging", result.TransformedTerraform);
    }

    [Fact]
    public void Transform_WithExistingTarget_PreservesTargetOnlyOperations()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"),
            ("custom-health-staging", "GET", "health")); // target-only operation

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Single(result.Summary.SyncedOperations);
        Assert.Single(result.Summary.PreservedOperations);
        Assert.Contains("GET health", result.Summary.PreservedOperations);
        // The preserved block should be in the output
        Assert.Contains("custom-health-staging", result.TransformedTerraform);
        Assert.Contains("health", result.TransformedTerraform);
    }

    [Fact]
    public void Transform_WithExistingTarget_FullMergeScenario()
    {
        // Source (dev): GET /users, POST /users, PUT /users/{id}
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users"),
            ("update-user-dev", "PUT", "users/{id}"));

        // Existing target (staging): GET /users, POST /users, GET /health (custom)
        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"),
            ("create-user-staging", "POST", "users"),
            ("custom-health-staging", "GET", "health"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);

        // 3 source + 1 preserved = 4 total
        Assert.Equal(4, result.Summary.TotalOperations);
        Assert.Equal(2, result.Summary.SyncedOperations.Count); // GET /users, POST /users
        Assert.Single(result.Summary.AddedOperations);           // PUT /users/{id}
        Assert.Single(result.Summary.PreservedOperations);       // GET /health

        Assert.Contains("PUT users/{id}", result.Summary.AddedOperations);
        Assert.Contains("GET health", result.Summary.PreservedOperations);
    }

    [Fact]
    public void Transform_WithExistingTarget_TransformedSourceUsesTargetEnvValues()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        // The output should have target env values, not source env values
        Assert.Contains("rg-apim-staging", result.TransformedTerraform);
        Assert.Contains("apim-company-staging", result.TransformedTerraform);
        Assert.DoesNotContain("rg-apim-dev", result.TransformedTerraform);
        Assert.DoesNotContain("apim-company-dev", result.TransformedTerraform);
    }

    // ── Error Handling ───────────────────────────────────────────────

    [Fact]
    public void Transform_NullSource_ReturnsFailure()
    {
        var result = _transformer.Transform(null!, StagingSettings);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("required"));
    }

    [Fact]
    public void Transform_EmptySource_ReturnsFailure()
    {
        var result = _transformer.Transform("", StagingSettings);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("required"));
    }

    [Fact]
    public void Transform_WhitespaceSource_ReturnsFailure()
    {
        var result = _transformer.Transform("   ", StagingSettings);

        Assert.False(result.Success);
    }

    [Fact]
    public void Transform_UndetectableEnvironment_ReturnsFailure()
    {
        var terraform = """
            some-group = {
              api = [
                {
                    name = "my-custom-api"
                }
              ]
            }
            """;

        var result = _transformer.Transform(terraform, StagingSettings);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("auto-detect"));
    }

    [Fact]
    public void Transform_SameSourceAndTargetEnv_ReturnsFailure()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var settings = new EnvironmentTransformSettings
        {
            TargetEnvironment = "dev",
            TargetStageGroupName = "rg-apim-dev",
            TargetApimName = "apim-company-dev",
            TargetApiGatewayHost = "api-dev.company.com"
        };

        var result = _transformer.Transform(source, settings);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("same"));
    }

    [Fact]
    public void Transform_MissingFieldValues_ReportsWarnings()
    {
        // Terraform with no apim_resource_group_name or apim_name
        var terraform = """
            name = "my-api-dev"
            path = "app.dev/v1/api"
            api_operations = [
            ]
            """;

        var result = _transformer.Transform(terraform, StagingSettings);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("apim_resource_group_name"));
        Assert.Contains(result.Warnings, w => w.Contains("apim_name"));
    }

    // ── Cross-Environment Matching ───────────────────────────────────

    [Fact]
    public void Transform_OperationIdsChangedButRoutesMatch_SyncsCorrectly()
    {
        // Source has "get-users-dev", target has "get-users-staging"
        // They match by route (GET users), not by ID
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-users-dev", "GET", "users"));

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Single(result.Summary.SyncedOperations);
        Assert.Contains("GET users", result.Summary.SyncedOperations);
    }

    [Fact]
    public void Transform_ComplexUrlTemplates_MatchCorrectly()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com",
            ("get-user-by-id-dev", "GET", "users/{userId}/profiles/{profileId}"),
            ("search-users-dev", "GET", "users/search"));

        var existingTarget = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-user-profile-staging", "GET", "users/{userId}/profiles/{profileId}"),
            ("find-users-staging", "GET", "users/search"));

        var result = _transformer.Transform(source, StagingSettings, existingTarget);

        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Equal(2, result.Summary.SyncedOperations.Count);
    }

    // ── Staging to Prod ──────────────────────────────────────────────

    [Fact]
    public void Transform_StagingToProd_TransformsCorrectly()
    {
        var source = BuildTerraform("staging", "rg-apim-staging", "apim-company-staging", "api-staging.company.com",
            ("get-users-staging", "GET", "users"));

        var result = _transformer.Transform(source, ProdSettings);

        Assert.True(result.Success);
        Assert.Equal("staging", result.DetectedSourceEnvironment);
        Assert.Contains("rg-apim-prod", result.TransformedTerraform);
        Assert.Contains("apim-company-prod", result.TransformedTerraform);
        Assert.Contains("api.company.com", result.TransformedTerraform);
        Assert.Contains("get-users-prod", result.TransformedTerraform);
        Assert.Contains("test-api-prod", result.TransformedTerraform);
        Assert.Contains("app.prod/v1/api", result.TransformedTerraform);
    }

    // ── No Operations ────────────────────────────────────────────────

    [Fact]
    public void Transform_NoOperations_TransformsApiBlockOnly()
    {
        var source = BuildTerraform("dev", "rg-apim-dev", "apim-company-dev", "api-dev.company.com");

        var result = _transformer.Transform(source, StagingSettings);

        Assert.True(result.Success);
        Assert.Contains("rg-apim-staging", result.TransformedTerraform);
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.TotalOperations);
    }
}
