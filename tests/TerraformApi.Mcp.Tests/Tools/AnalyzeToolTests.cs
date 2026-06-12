using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>Tests for the analyze_terraform_apim MCP tool (§REV-2.7).</summary>
public class AnalyzeToolTests
{
    private readonly ISyncOrchestrator _orchestrator;

    private const string TemplatedTerraform = """
        apis = {
          bpc_apis = {
            backend_apis = {
              "${api_group_name}" = {
                product = []
                api = [
                  {
                    apim_resource_group_name = "${stage_group_name}"
                    apim_name                = "${apim_name}"
                    name                     = "${api_name}-${env}"
                    display_name             = "${api_display_name} - ${env}"
                    path                     = "${api_path_prefix}.${env}/v1/${api_path_suffix}"
                    service_url              = "https://${api_gateway_host}/v1/svc/"
                  },
                ]
                api_operations = [
                  {
                    operation_id             = "${operation_prefix}-${env}"
                    apim_resource_group_name = "${stage_group_name}"
                    apim_name                = "${apim_name}"
                    api_name                 = "${api_name}-${env}"
                    display_name             = "${operation_display_name}"
                    method                   = "GET"
                    url_template             = "${operation_path}"
                    status_code              = "200"
                    description              = ""
                  },
                ]
              }
            }
          }
        }
        """;

    private const string LiteralTerraform = """
        my-api = {
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
            },
          ]
        }
        """;

    public AnalyzeToolTests()
    {
        _orchestrator = BuildOrchestrator();
    }

    internal static ISyncOrchestrator BuildOrchestrator()
    {
        var validator = new ApimNamingValidatorService();
        var openApiParser = new OpenApiParserService(validator);
        var hclParser = new HclParserService();
        var reader = new ApimTerraformReaderService(hclParser);
        var hclWriter = new HclWriterService();
        var commentBuilder = new OperationCommentBuilderService();
        var writer = new ApimTerraformWriterService(hclWriter, reader, commentBuilder);
        var resolver = new TerraformInterpolationResolver();
        var synchronizer = new AppendOnlySynchronizerService(
            new OperationMatcherService(resolver),
            new DuplicateDetectorService(),
            new ApimTemplateProfileDetectorService(),
            commentBuilder,
            hclWriter,
            resolver,
            NullLogger<AppendOnlySynchronizerService>.Instance);

        return new SyncOrchestratorService(
            openApiParser, reader, writer, hclParser, synchronizer,
            new ApimTemplateProfileDetectorService(),
            new DuplicateDetectorService(),
            new ApimTemplateProfileApplierService(resolver));
    }

    [Fact]
    public void Analyze_EmptyInput_ReturnsError()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, "");
        using var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public void Analyze_InvalidHcl_ReturnsError()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, "not { valid hcl");
        using var doc = JsonDocument.Parse(result);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Analyze_TemplatedFile_HighlyTemplated()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, TemplatedTerraform);
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("HighlyTemplated",
            doc.RootElement.GetProperty("detectedProfile").GetProperty("confidence").GetString());
        Assert.Equal("UserExampleProfile",
            doc.RootElement.GetProperty("detectedProfile").GetProperty("closestKnownProfileName").GetString());
    }

    [Fact]
    public void Analyze_LiteralFile_MostlyLiteral()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, LiteralTerraform);
        using var doc = JsonDocument.Parse(result);

        Assert.Equal("MostlyLiteral",
            doc.RootElement.GetProperty("detectedProfile").GetProperty("confidence").GetString());
    }

    [Fact]
    public void Analyze_ValidApiGroups_WithCounts()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, TemplatedTerraform);
        using var doc = JsonDocument.Parse(result);

        var groups = doc.RootElement.GetProperty("apiGroups");
        Assert.Equal(1, groups.GetArrayLength());
        Assert.Equal("${stage_group_name}", groups[0].GetProperty("apimResourceGroupName").GetString());
        Assert.Equal("${api_name}-${env}", groups[0].GetProperty("apiName").GetString());
        Assert.Equal(1, groups[0].GetProperty("operationCount").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public void Analyze_FileWithDuplicates_Reported()
    {
        var terraform = """
            g = {
              api_operations = [
                {
                  operation_id = "dup"
                  method       = "GET"
                  url_template = "/a"
                  api_name     = "x"
                },
                {
                  operation_id = "dup"
                  method       = "POST"
                  url_template = "/b"
                  api_name     = "x"
                },
              ]
            }
            """;

        var result = AnalyzeTool.AnalyzeCore(_orchestrator, terraform);
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("duplicates").GetArrayLength() > 0);
    }

    [Fact]
    public void Analyze_TwoGroups_BothReported()
    {
        var terraform = """
            group-one = {
              api_operations = [
                {
                  operation_id = "a"
                  method       = "GET"
                  url_template = "/a"
                  apim_resource_group_name = "rg-one"
                  api_name     = "api-one"
                },
              ]
            }
            group-two = {
              api_operations = [
                {
                  operation_id = "b"
                  method       = "GET"
                  url_template = "/b"
                  apim_resource_group_name = "rg-two"
                  api_name     = "api-two"
                },
              ]
            }
            """;

        var result = AnalyzeTool.AnalyzeCore(_orchestrator, terraform);
        using var doc = JsonDocument.Parse(result);

        Assert.Equal(2, doc.RootElement.GetProperty("apiGroups").GetArrayLength());
        Assert.Equal(2, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public void Analyze_AllReferencedVariables_Present()
    {
        var result = AnalyzeTool.AnalyzeCore(_orchestrator, TemplatedTerraform);
        using var doc = JsonDocument.Parse(result);

        var vars = doc.RootElement.GetProperty("detectedProfile")
            .GetProperty("allReferencedVariables")
            .EnumerateArray()
            .Select(v => v.GetString())
            .ToList();

        Assert.Contains("stage_group_name", vars);
        Assert.Contains("apim_name", vars);
        Assert.Contains("env", vars);
        Assert.Contains("operation_prefix", vars);
    }
}
