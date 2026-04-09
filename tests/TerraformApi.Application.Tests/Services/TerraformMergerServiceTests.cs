using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

public class TerraformMergerServiceTests
{
    private readonly TerraformGeneratorService _generator = new();
    private readonly TerraformMergerService _merger;

    public TerraformMergerServiceTests()
    {
        _merger = new TerraformMergerService(_generator);
    }

    private static ApimConfiguration CreateConfig(params (string id, string method, string url)[] operations)
    {
        return new ApimConfiguration
        {
            ApiGroupName = "test-group",
            Api = new ApimApi
            {
                ApimResourceGroupName = "rg",
                ApimName = "apim",
                Name = "api-dev",
                DisplayName = "API - dev",
                Path = "app.dev/v1/api",
                ServiceUrl = "https://gw.test.com/v1/svc/"
            },
            ApiOperations = operations.Select(o => new ApimApiOperation
            {
                OperationId = o.id,
                ApimResourceGroupName = "rg",
                ApimName = "apim",
                ApiName = "api-dev",
                DisplayName = $"{o.method} {o.url}",
                Method = o.method,
                UrlTemplate = o.url
            }).ToList()
        };
    }

    [Fact]
    public void Merge_NoExistingOperations_ReturnsNewConfig()
    {
        var existing = "";
        var newConfig = CreateConfig(("get-users-dev", "GET", "users"));

        var result = _merger.Merge(existing, newConfig);

        Assert.Contains("get-users-dev", result);
    }

    [Fact]
    public void Merge_SameOperations_ReturnsNewConfig()
    {
        var existingConfig = CreateConfig(("get-users-dev", "GET", "users"));
        var existing = _generator.Generate(existingConfig);

        var newConfig = CreateConfig(("get-users-dev", "GET", "users"));

        var result = _merger.Merge(existing, newConfig);

        Assert.Contains("get-users-dev", result);
    }

    [Fact]
    public void Merge_NewOperationAdded_IncludesBoth()
    {
        var existingConfig = CreateConfig(("get-users-dev", "GET", "users"));
        var existing = _generator.Generate(existingConfig);

        var newConfig = CreateConfig(
            ("get-users-dev", "GET", "users"),
            ("create-user-dev", "POST", "users")
        );

        var result = _merger.Merge(existing, newConfig);

        Assert.Contains("get-users-dev", result);
        Assert.Contains("create-user-dev", result);
    }

    [Fact]
    public void Merge_CustomOperationPreserved_WhenNotInNewSpec()
    {
        var existingConfig = CreateConfig(
            ("get-users-dev", "GET", "users"),
            ("custom-health-dev", "GET", "health")
        );
        var existing = _generator.Generate(existingConfig);

        // New spec only has get-users, custom-health should be preserved
        var newConfig = CreateConfig(("get-users-dev", "GET", "users"));

        var result = _merger.Merge(existing, newConfig);

        Assert.Contains("get-users-dev", result);
        Assert.Contains("custom-health-dev", result);
    }

    [Fact]
    public void Merge_ExistingOperationRemoved_WhenInNewSpec()
    {
        var existingConfig = CreateConfig(
            ("get-users-dev", "GET", "users"),
            ("delete-user-dev", "DELETE", "users/{id}")
        );
        var existing = _generator.Generate(existingConfig);

        // New spec replaces both operations with updated versions
        var newConfig = CreateConfig(
            ("get-users-dev", "GET", "users"),
            ("delete-user-dev", "DELETE", "users/{id}")
        );

        var result = _merger.Merge(existing, newConfig);

        // Both should exist (replaced by new versions)
        Assert.Contains("get-users-dev", result);
        Assert.Contains("delete-user-dev", result);
    }
}
