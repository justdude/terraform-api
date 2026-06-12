using System.Text.Json;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>Tests for the apply_template_profile MCP tool (§REV-2.7).</summary>
public class ApplyTemplateProfileToolTests
{
    private readonly ISyncOrchestrator _orchestrator = AnalyzeToolTests.BuildOrchestrator();

    private const string LiteralTerraform = """
        g = {
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

    private const string TemplatedTerraform = """
        g = {
          api_operations = [
            {
              operation_id             = "${operation_prefix}-${env}"
              apim_resource_group_name = "${stage_group_name}"
              apim_name                = "${apim_name}"
              api_name                 = "${api_name}-${env}"
              display_name             = "Get users"
              method                   = "GET"
              url_template             = "users"
              status_code              = "200"
              description              = ""
            },
          ]
        }
        """;

    [Fact]
    public void Apply_Templatize_ReplacesWithOverwrite()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            LiteralTerraform,
            direction: "Templatize",
            profileName: "UserExampleProfile",
            overwriteExisting: true);

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("${apim_name}", hcl);
        Assert.Contains("${stage_group_name}", hcl);
        Assert.True(doc.RootElement.GetProperty("appliedChanges").GetArrayLength() > 0);
    }

    [Fact]
    public void Apply_TemplatizeWithoutOverwrite_KeepsLiterals()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            LiteralTerraform,
            direction: "Templatize",
            profileName: "UserExampleProfile",
            overwriteExisting: false);

        using var doc = JsonDocument.Parse(result);
        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;

        // Literals with values stay.
        Assert.Contains("apim-company-dev", hcl);
        Assert.Contains("get-users-dev", hcl);
    }

    [Fact]
    public void Apply_Resolve_SubstitutesVariables()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            TemplatedTerraform,
            direction: "Resolve",
            variableContextJson: """{"stage_group_name":"rg-apim-dev","apim_name":"apim-company-dev","api_name":"bpc","env":"dev","operation_prefix":"get-users"}""");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var hcl = doc.RootElement.GetProperty("terraformConfig").GetString()!;
        Assert.Contains("\"rg-apim-dev\"", hcl);
        Assert.Contains("\"get-users-dev\"", hcl);
        Assert.Contains("\"bpc-dev\"", hcl);
    }

    [Fact]
    public void Apply_ResolveWithMissingVariable_Warns()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            TemplatedTerraform,
            direction: "Resolve",
            variableContextJson: """{"env":"dev"}""");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("warnings").GetArrayLength() > 0);
    }

    [Fact]
    public void Apply_UnknownProfile_ReturnsError()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            LiteralTerraform,
            direction: "Templatize",
            profileName: "NoSuchProfile");

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Apply_UnknownDirection_ReturnsError()
    {
        var result = ApplyTemplateProfileTool.Apply(
            _orchestrator,
            LiteralTerraform,
            direction: "Sideways");

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }
}
