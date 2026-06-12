using TerraformApi.Application.Services;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Placeholder-default behavior: missing APIM settings never block generation —
/// the output carries {tag} placeholders plus an explanatory header comment.
/// </summary>
public class ApimPlaceholdersTests
{
    private const string MinimalOpenApi = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Demo API", "version": "1.0.0" },
          "paths": {
            "/things": {
              "get": {
                "operationId": "listThings",
                "summary": "List things",
                "responses": { "200": { "description": "OK" } }
              }
            }
          }
        }
        """;

    private static ConversionOrchestratorService BuildOrchestrator()
    {
        var validator = new ApimNamingValidatorService();
        var parser = new OpenApiParserService(validator);
        var generator = new TerraformGeneratorService();
        var merger = new TerraformMergerService(generator);
        return new ConversionOrchestratorService(parser, generator, merger, validator);
    }

    [Fact]
    public void Normalize_EmptySettings_AllTagsApplied()
    {
        var (normalized, tags) = ApimPlaceholders.Normalize(new ConversionSettings());

        Assert.Equal("{environment}", normalized.Environment);
        Assert.Equal("{api-group}", normalized.ApiGroupName);
        Assert.Equal("{stage-group-name}", normalized.StageGroupName);
        Assert.Equal("{apim-name}", normalized.ApimName);
        Assert.Equal("{api-path-prefix}", normalized.ApiPathPrefix);
        Assert.Equal("{api-path-suffix}", normalized.ApiPathSuffix);
        Assert.Equal("{api-gateway-host}", normalized.ApiGatewayHost);
        Assert.Equal("{backend-service-path}", normalized.BackendServicePath);
        Assert.Equal(8, tags.Count);
    }

    [Fact]
    public void Normalize_Idempotent_NoNewTagsOnSecondPass()
    {
        var (first, _) = ApimPlaceholders.Normalize(new ConversionSettings());
        var (_, secondTags) = ApimPlaceholders.Normalize(first);

        Assert.Empty(secondTags);
    }

    [Fact]
    public void Normalize_ProvidedValues_Untouched()
    {
        var settings = new ConversionSettings
        {
            Environment = "dev",
            ApiGroupName = "my-group",
            StageGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            ApiPathPrefix = "myapp",
            ApiPathSuffix = "api",
            ApiGatewayHost = "api.dev.company.com",
            BackendServicePath = "my-service"
        };

        var (normalized, tags) = ApimPlaceholders.Normalize(settings);

        Assert.Empty(tags);
        Assert.Equal("dev", normalized.Environment);
        Assert.Equal("my-group", normalized.ApiGroupName);
    }

    [Fact]
    public void Convert_NoSettingsProvided_SucceedsWithTagsAndHeader()
    {
        var result = BuildOrchestrator().Convert(MinimalOpenApi, new ConversionSettings());

        Assert.True(result.Success, string.Join("; ", result.Errors));

        // Header comment explains every tag.
        Assert.Contains("GENERATED WITH PLACEHOLDER TAGS", result.TerraformConfig);
        Assert.Contains("{api-group}", result.TerraformConfig);
        Assert.Contains("{environment}", result.TerraformConfig);

        // The tags are used in the actual configuration values.
        Assert.Contains("{api-group} = {", result.TerraformConfig);
        Assert.Contains("\"{stage-group-name}\"", result.TerraformConfig);
        Assert.Contains("\"{apim-name}\"", result.TerraformConfig);
    }

    [Fact]
    public void Convert_PlaceholderEnvironment_KeptIntactInOperationIds()
    {
        var result = BuildOrchestrator().Convert(MinimalOpenApi, new ConversionSettings());

        Assert.True(result.Success);
        // The sanitizer must not strip the tag's braces from operation ids.
        var op = result.Configuration!.ApiOperations.Single();
        Assert.EndsWith("-{environment}", op.OperationId);
    }

    [Fact]
    public void Convert_PartialSettings_OnlyMissingOnesTagged()
    {
        var settings = new ConversionSettings
        {
            Environment = "dev",
            ApiGroupName = "my-group",
            StageGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            ApiPathPrefix = "myapp",
            ApiPathSuffix = "api",
            ApiGatewayHost = "api.dev.company.com"
            // BackendServicePath missing
        };

        var result = BuildOrchestrator().Convert(MinimalOpenApi, settings);

        Assert.True(result.Success);
        Assert.Contains("{backend-service-path}", result.TerraformConfig);
        Assert.DoesNotContain("{api-group}", result.TerraformConfig);
        Assert.Contains("my-group = {", result.TerraformConfig);
    }

    [Fact]
    public void Convert_FullSettings_NoHeaderComment()
    {
        var settings = new ConversionSettings
        {
            Environment = "dev",
            ApiGroupName = "my-group",
            StageGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            ApiPathPrefix = "myapp",
            ApiPathSuffix = "api",
            ApiGatewayHost = "api.dev.company.com",
            BackendServicePath = "my-service"
        };

        var result = BuildOrchestrator().Convert(MinimalOpenApi, settings);

        Assert.True(result.Success);
        Assert.DoesNotContain("GENERATED WITH PLACEHOLDER TAGS", result.TerraformConfig);
    }

    [Fact]
    public void Update_NoSettingsProvided_SucceedsAndWarns()
    {
        var orchestrator = BuildOrchestrator();
        var initial = orchestrator.Convert(MinimalOpenApi, new ConversionSettings());
        Assert.True(initial.Success);

        // Strip the tag header so the merger sees plain HCL-ish text.
        var existing = string.Join("\n",
            initial.TerraformConfig.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));

        var result = orchestrator.Update(MinimalOpenApi, existing, new ConversionSettings());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("{api-group}", result.TerraformConfig);
        Assert.Contains(result.Warnings, w => w.Contains("placeholder tag"));
    }

    [Fact]
    public void BuildHeaderComment_EmptyTags_EmptyString()
    {
        Assert.Equal("", ApimPlaceholders.BuildHeaderComment([]));
    }

    [Fact]
    public void ContainsPlaceholder_DetectsKnownTags()
    {
        Assert.True(ApimPlaceholders.ContainsPlaceholder("{api-group}"));
        Assert.True(ApimPlaceholders.ContainsPlaceholder("prefix-{environment}"));
        Assert.False(ApimPlaceholders.ContainsPlaceholder("my-api-dev"));
        Assert.False(ApimPlaceholders.ContainsPlaceholder(null));
    }
}
