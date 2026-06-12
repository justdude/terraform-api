using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Apim;

/// <summary>
/// Writer tests T1–T3 (§REV-1.7 Phase 3) and BuildFromConfiguration structure tests.
/// </summary>
public class ApimTerraformWriterServiceTests
{
    private readonly HclParserService _parser = new();
    private readonly ApimTerraformWriterService _writer;

    public ApimTerraformWriterServiceTests()
    {
        var hclWriter = new HclWriterService();
        var reader = new ApimTerraformReaderService(_parser);
        _writer = new ApimTerraformWriterService(hclWriter, reader, new OperationCommentBuilderService());
    }

    private static ApimConfiguration SampleConfiguration(int operations = 1) => new()
    {
        ApiGroupName = "${api_group_name}",
        Api = new ApimApi
        {
            ApimResourceGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            Name = "my-api-dev",
            DisplayName = "My API - dev",
            Path = "myapp.dev/v1/api",
            ServiceUrl = "https://api-dev.company.com/v1/my-service/"
        },
        ApiOperations = Enumerable.Range(1, operations).Select(i => new ApimApiOperation
        {
            OperationId = $"getThing{i}",
            ApimResourceGroupName = "rg-apim-dev",
            ApimName = "apim-company-dev",
            ApiName = "my-api-dev",
            DisplayName = $"Get thing {i}",
            Method = "GET",
            UrlTemplate = $"/things/{i}",
            Description = "Returns a thing"
        }).ToList()
    };

    // T1
    [Fact]
    public void BuildFromConfiguration_UserExampleProfile_AstContainsProfilePlaceholders()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), new BuildOptions());
        var group = Assert.Single(parsed.ApiGroups);
        var api = Assert.Single(group.Apis);

        Assert.Equal("${stage_group_name}", api.ApimResourceGroupName.StructuralText);
        Assert.Equal("${apim_name}", api.ApimName.StructuralText);
        Assert.Equal("${api_name}-${env}", api.Name.StructuralText);
        Assert.Equal("${api_path_prefix}.${env}/v1/${api_path_suffix}", api.Path.StructuralText);

        var op = group.Operations.Single();
        Assert.Equal("${operation_prefix}-${env}", op.OperationId.StructuralText);
        Assert.Equal("${stage_group_name}", op.ApimResourceGroupName?.StructuralText);

        // Routing fields stay literal — they are the API contract.
        Assert.Equal("GET", op.Method.StructuralText);
        Assert.Equal("/things/1", op.UrlTemplate.StructuralText);
    }

    // T2
    [Fact]
    public void BuildFromConfiguration_LiteralProfile_NoInterpolations()
    {
        var options = new BuildOptions
        {
            Profile = ApimTemplateProfile.LiteralProfile,
            AddReplaceBeforeApplyHeader = false
        };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        var group = Assert.Single(parsed.ApiGroups);
        var api = Assert.Single(group.Apis);

        Assert.Equal("rg-apim-dev", api.ApimResourceGroupName.StructuralText);
        Assert.False(api.ApimResourceGroupName.IsInterpolated);

        var op = group.Operations.Single();
        Assert.Equal("getThing1", op.OperationId.StructuralText);
        Assert.False(op.OperationId.IsInterpolated);
    }

    // T3
    [Fact]
    public void BuildFromConfiguration_EachOperationHasLeadingComments()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(2), new BuildOptions());
        var group = Assert.Single(parsed.ApiGroups);

        var opsArray = Assert.IsType<HclArray>(group.AstNode.Get("api_operations"));
        Assert.All(opsArray.Items, item =>
        {
            Assert.InRange(item.LeadingComments.Count, 2, 3);
            // First line: " METHOD URL | op_id: ID"
            Assert.Matches(@"^ (GET|POST|PUT|DELETE|PATCH) \S+ \| op_id: ", item.LeadingComments[0].Text);
        });
    }

    [Fact]
    public void BuildFromConfiguration_TemplatedOps_HaveThreeCommentsWithPlaceholders()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), new BuildOptions());
        var opsArray = (HclArray)parsed.ApiGroups.Single().AstNode.Get("api_operations")!;
        var comments = opsArray.Items[0].LeadingComments;

        Assert.Equal(3, comments.Count);
        Assert.Contains("placeholders to replace:", comments[2].Text);
        Assert.Contains("${operation_prefix}", comments[2].Text);
        Assert.Contains("${env}", comments[2].Text);
    }

    [Fact]
    public void BuildFromConfiguration_LiteralProfile_OnlyTwoComments()
    {
        var options = new BuildOptions
        {
            Profile = ApimTemplateProfile.LiteralProfile,
            AddReplaceBeforeApplyHeader = false
        };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        var opsArray = (HclArray)parsed.ApiGroups.Single().AstNode.Get("api_operations")!;

        Assert.Equal(2, opsArray.Items[0].LeadingComments.Count);
    }

    [Fact]
    public void BuildFromConfiguration_DefaultParentPath_NestedStructure()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), new BuildOptions());
        Assert.Equal(["apis", "bpc_apis", "backend_apis"], parsed.ApiGroupParentPath);
    }

    [Fact]
    public void BuildFromConfiguration_CustomParentPath_GeneratesNestedStructure()
    {
        var options = new BuildOptions { ApiGroupParentPath = ["apis", "backend_apis"] };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        Assert.Equal(["apis", "backend_apis"], parsed.ApiGroupParentPath);
    }

    [Fact]
    public void BuildFromConfiguration_FlatPath_GroupAtRoot()
    {
        var options = new BuildOptions { ApiGroupParentPath = [] };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        Assert.Null(parsed.ApiGroupParentPath);
        Assert.Single(parsed.ApiGroups);
    }

    [Fact]
    public void BuildFromConfiguration_ProducesValidParseableHcl()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(3), new BuildOptions());
        var hcl = _writer.Write(parsed);

        var reparsed = _parser.Parse(hcl);
        Assert.NotNull(reparsed);

        var reader = new ApimTerraformReaderService(_parser);
        var reread = reader.Read(reparsed);
        Assert.Single(reread.ApiGroups);
        Assert.Equal(3, reread.ApiGroups[0].Operations.Count);
    }

    [Fact]
    public void BuildFromConfiguration_ReplaceBeforeApplyHeader_Present()
    {
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), new BuildOptions());
        var hcl = _writer.Write(parsed);

        Assert.Contains("REPLACE BEFORE APPLY", hcl);
        Assert.Contains("${stage_group_name}", hcl);
    }

    [Fact]
    public void BuildFromConfiguration_HeaderDisabled_NotPresent()
    {
        var options = new BuildOptions { AddReplaceBeforeApplyHeader = false };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        var hcl = _writer.Write(parsed);

        Assert.DoesNotContain("REPLACE BEFORE APPLY", hcl);
    }

    [Fact]
    public void BuildFromConfiguration_ExtendedProfile_OpSubstitutionInOperationId()
    {
        var options = new BuildOptions { Profile = ApimTemplateProfile.ExtendedProfile };
        var parsed = _writer.BuildFromConfiguration(SampleConfiguration(), options);
        var op = parsed.ApiGroups.Single().Operations.Single();

        Assert.Equal("${operation_prefix}-get-thing1-${env}", op.OperationId.StructuralText);
    }

    [Fact]
    public void BuildFromConfiguration_WithPolicy_EmitsHeredoc()
    {
        var config = SampleConfiguration() with
        {
            Api = SampleConfiguration().Api with { Policy = "<policies>\n  <inbound />\n</policies>" }
        };
        var parsed = _writer.BuildFromConfiguration(config, new BuildOptions());
        var hcl = _writer.Write(parsed);

        Assert.Contains("policy", hcl);
        Assert.Contains("<<XML", hcl);
        Assert.Contains("<policies>", hcl);
    }

    [Theory]
    [InlineData("plain-name", false)]
    [InlineData("plain_name2", false)]
    [InlineData("${api_group_name}", true)]
    [InlineData("{api-group}", true)]
    [InlineData("has space", true)]
    [InlineData("dotted.name", true)]
    public void NeedsQuotedKey_QuotesNonIdentifiers(string key, bool expected)
    {
        Assert.Equal(expected, ApimTerraformWriterService.NeedsQuotedKey(key));
    }

    [Fact]
    public void BuildFromConfiguration_PlaceholderGroupName_QuotedAndParseable()
    {
        var config = SampleConfiguration() with { ApiGroupName = "{api-group}" };
        var parsed = _writer.BuildFromConfiguration(config, new BuildOptions());
        var hcl = _writer.Write(parsed);

        Assert.Contains("\"{api-group}\"", hcl);

        var reparsed = _parser.Parse(hcl);
        Assert.NotNull(reparsed);
    }

    [Theory]
    [InlineData("listUserById", "list-user-by-id")]
    [InlineData("getUsers", "get-users")]
    [InlineData("already-kebab", "already-kebab")]
    [InlineData("with spaces", "with-spaces")]
    [InlineData("HTMLParser", "h-t-m-l-parser")]
    public void ToKebabCase_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, ApimTerraformWriterService.ToKebabCase(input));
    }
}
