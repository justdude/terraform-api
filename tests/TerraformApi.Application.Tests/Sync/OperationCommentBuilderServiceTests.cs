using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>Comment builder tests CB1–CB4 (§REV-1.7 Phase 4b).</summary>
public class OperationCommentBuilderServiceTests
{
    private readonly OperationCommentBuilderService _builder = new();

    // CB1
    [Fact]
    public void Build_NoPlaceholders_TwoComments()
    {
        var comments = _builder.Build(new OperationCommentSpec
        {
            Method = "GET",
            UrlTemplate = "/users",
            OperationId = "getUsers",
            Source = OperationCommentSource.OpenApi
        });

        Assert.Equal(2, comments.Count);
    }

    // CB2
    [Fact]
    public void Build_WithPlaceholders_ThreeCommentsWithCorrectList()
    {
        var comments = _builder.Build(new OperationCommentSpec
        {
            Method = "GET",
            UrlTemplate = "/users/{id}",
            OperationId = "${operation_prefix}-${env}",
            Source = OperationCommentSource.OpenApi,
            PlaceholdersToReplace = ["env", "operation_prefix", "stage_group_name"]
        });

        Assert.Equal(3, comments.Count);
        Assert.Contains("placeholders to replace:", comments[2].Text);
        Assert.Contains("${env}", comments[2].Text);
        Assert.Contains("${operation_prefix}", comments[2].Text);
        Assert.Contains("${stage_group_name}", comments[2].Text);
    }

    // CB3
    [Fact]
    public void ExtractPlaceholders_FiveUniqueNames_SortedAndDeduplicated()
    {
        var node = new HclObject
        {
            Items =
            [
                new HclAssignment { Key = "operation_id", Value = Interp("${e}-${d}") },
                new HclAssignment { Key = "apim_name", Value = Interp("${c}") },
                new HclAssignment { Key = "api_name", Value = Interp("${b}-${a}") },
                new HclAssignment { Key = "again", Value = Interp("${a}") } // duplicate of a
            ]
        };

        var result = _builder.ExtractPlaceholders(node);

        Assert.Equal(["a", "b", "c", "d", "e"], result);
    }

    // CB4
    [Fact]
    public void Build_FirstLine_ExactlyMethodUrlOpId()
    {
        var comments = _builder.Build(new OperationCommentSpec
        {
            Method = "get",
            UrlTemplate = "/users/{id}",
            OperationId = "getUserById",
            DisplayName = "Get user by id",
            Source = OperationCommentSource.OpenApi
        });

        Assert.Equal(" GET /users/{id} | op_id: getUserById", comments[0].Text);
        Assert.Equal(HclCommentKind.LineHash, comments[0].Kind);
        Assert.True(comments[0].IsLeading);
    }

    [Fact]
    public void Build_SecondLine_ContainsDisplayNameSourceAndDate()
    {
        var date = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var comments = _builder.Build(new OperationCommentSpec
        {
            Method = "GET",
            UrlTemplate = "/users",
            OperationId = "getUsers",
            DisplayName = "Get users",
            Source = OperationCommentSource.OpenApi,
            InsertedAt = date
        });

        Assert.Contains("display_name: \"Get users\"", comments[1].Text);
        Assert.Contains("source: OpenApi", comments[1].Text);
        Assert.Contains("inserted: 2026-06-12", comments[1].Text);
    }

    [Fact]
    public void ExtractPlaceholders_NestedObjectsAndArrays_Collected()
    {
        var node = new HclObject
        {
            Items =
            [
                new HclAssignment
                {
                    Key = "request",
                    Value = new HclArray
                    {
                        Items =
                        [
                            new HclArrayItem
                            {
                                Value = new HclObject
                                {
                                    Items = [new HclAssignment { Key = "name", Value = Interp("${nested_var}") }]
                                }
                            }
                        ]
                    }
                }
            ]
        };

        Assert.Equal(["nested_var"], _builder.ExtractPlaceholders(node));
    }

    [Fact]
    public void ExtractPlaceholders_HeredocContent_Collected()
    {
        var node = new HclObject
        {
            Items =
            [
                new HclAssignment
                {
                    Key = "policy",
                    Value = new HclHeredoc { Marker = "XML", Content = "<origin>https://${frontend_host}.${env}</origin>" }
                }
            ]
        };

        Assert.Equal(["env", "frontend_host"], _builder.ExtractPlaceholders(node));
    }

    private static HclInterpolation Interp(string text) => new()
    {
        InnerText = text,
        ReferencedExpressions = TerraformApi.Application.Services.Hcl.HclParserService.ExtractReferences(text)
    };
}
