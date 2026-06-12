using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Apim;

/// <summary>
/// Reader tests R1–R6 (§5.3) and group-index tests T4–T5 (§REV-1.7 Phase 3).
/// </summary>
public class ApimTerraformReaderServiceTests
{
    private readonly ApimTerraformReaderService _reader = new(new HclParserService());

    private static string LoadFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "example-existing.tf"));

    // R1
    [Fact]
    public void Read_FlatRoot_OneApiGroup()
    {
        var parsed = _reader.Read("""
            my-api = {
              api = [
                {
                  name = "my-api-dev"
                  apim_resource_group_name = "rg"
                },
              ]
              api_operations = [
                {
                  operation_id = "get-x"
                  method       = "GET"
                  url_template = "x"
                  apim_resource_group_name = "rg"
                  api_name     = "my-api-dev"
                },
              ]
            }
            """);

        var group = Assert.Single(parsed.ApiGroups);
        Assert.Equal("my-api", group.ApiGroupName);
        Assert.Null(parsed.ApiGroupParentPath);
        Assert.Single(group.Apis);
        Assert.Single(group.Operations);
    }

    // R2
    [Fact]
    public void Read_NestedStructure_ParentPathSet()
    {
        var parsed = _reader.Read(LoadFixture());

        Assert.Equal(["apis", "bpc_apis", "backend_apis"], parsed.ApiGroupParentPath);
        var group = Assert.Single(parsed.ApiGroups);
        Assert.Equal("${api_group_name}", group.ApiGroupName);
        Assert.True(group.KeyIsQuoted);
    }

    // R3
    [Fact]
    public void Read_MultipleGroupsUnderOneParent_AllRecognized()
    {
        var parsed = _reader.Read("""
            apis = {
              backend_apis = {
                group-one = {
                  api = []
                  api_operations = []
                }
                group-two = {
                  api = []
                  api_operations = []
                }
              }
            }
            """);

        Assert.Equal(2, parsed.ApiGroups.Count);
        Assert.Equal(["apis", "backend_apis"], parsed.ApiGroupParentPath);
    }

    // R4
    [Fact]
    public void Read_NoApiOperations_GroupExistsOperationsEmpty()
    {
        var parsed = _reader.Read("""
            my-api = {
              api = [
                {
                  name = "x"
                },
              ]
            }
            """);

        var group = Assert.Single(parsed.ApiGroups);
        Assert.Single(group.Apis);
        Assert.Empty(group.Operations);
    }

    // R5
    [Fact]
    public void Read_NoApi_GroupExistsApisEmpty()
    {
        var parsed = _reader.Read("""
            my-api = {
              api_operations = [
                {
                  operation_id = "x"
                  method       = "GET"
                  url_template = "y"
                },
              ]
            }
            """);

        var group = Assert.Single(parsed.ApiGroups);
        Assert.Empty(group.Apis);
        Assert.Single(group.Operations);
    }

    // R6
    [Fact]
    public void Read_PolicyHeredocWithMethodTags_NotExtractedAsOperations()
    {
        var parsed = _reader.Read(LoadFixture());

        var group = Assert.Single(parsed.ApiGroups);
        // Exactly one operation — the <method>GET</method> etc. inside the CORS
        // policy heredoc must not leak into the operations list.
        Assert.Single(group.Operations);
        Assert.Equal("GET", group.Operations[0].Method.StructuralText);

        // The policy itself is available on the api block.
        var api = Assert.Single(group.Apis);
        Assert.NotNull(api.Policy);
        Assert.Contains("<method>POST</method>", api.Policy!.StructuralText);
    }

    [Fact]
    public void Read_Fixture_OperationFieldsExtracted()
    {
        var parsed = _reader.Read(LoadFixture());
        var op = parsed.ApiGroups.Single().Operations.Single();

        Assert.Equal("${operation_prefix}-${env}", op.OperationId.StructuralText);
        Assert.True(op.OperationId.IsInterpolated);
        Assert.Equal("GET", op.Method.StructuralText);
        Assert.Equal("${operation_path}", op.UrlTemplate.StructuralText);
        Assert.Equal("${stage_group_name}", op.ApimResourceGroupName?.StructuralText);
        Assert.Equal("${api_name}-${env}", op.ApiName?.StructuralText);
        Assert.Equal("200", op.StatusCode?.StructuralText);
    }

    // T4
    [Fact]
    public void Read_Fixture_BuildsApisByGroupKey()
    {
        var parsed = _reader.Read(LoadFixture());

        var (key, bundle) = Assert.Single(parsed.ApisByGroupKey);
        Assert.Equal("${stage_group_name}", key.ApimResourceGroupNameRaw);
        Assert.Equal("${api_name}-${env}", key.ApiNameRaw);
        Assert.NotNull(bundle.Api);
        Assert.Single(bundle.Operations);
    }

    // T5
    [Fact]
    public void Read_TwoDistinctRgApiPairs_TwoBundles()
    {
        var parsed = _reader.Read("""
            my-group = {
              api = [
                {
                  name = "api-one"
                  apim_resource_group_name = "rg-one"
                },
                {
                  name = "api-two"
                  apim_resource_group_name = "rg-two"
                },
              ]
              api_operations = [
                {
                  operation_id = "op1"
                  method       = "GET"
                  url_template = "a"
                  apim_resource_group_name = "rg-one"
                  api_name     = "api-one"
                },
                {
                  operation_id = "op2"
                  method       = "GET"
                  url_template = "b"
                  apim_resource_group_name = "rg-two"
                  api_name     = "api-two"
                },
                {
                  operation_id = "op3"
                  method       = "POST"
                  url_template = "b"
                  apim_resource_group_name = "rg-two"
                  api_name     = "api-two"
                },
              ]
            }
            """);

        Assert.Equal(2, parsed.ApisByGroupKey.Count);

        var keyOne = new ApimApiGroupKey { ApimResourceGroupNameRaw = "rg-one", ApiNameRaw = "api-one" };
        var keyTwo = new ApimApiGroupKey { ApimResourceGroupNameRaw = "rg-two", ApiNameRaw = "api-two" };

        Assert.Single(parsed.ApisByGroupKey[keyOne].Operations);
        Assert.Equal(2, parsed.ApisByGroupKey[keyTwo].Operations.Count);
    }

    [Fact]
    public void Read_EmptyDocument_NoGroups()
    {
        var parsed = _reader.Read("");
        Assert.Empty(parsed.ApiGroups);
        Assert.Empty(parsed.ApisByGroupKey);
    }

    [Fact]
    public void Read_OperationWithRequestResponse_ArraysExposed()
    {
        var parsed = _reader.Read("""
            g = {
              api_operations = [
                {
                  operation_id = "op"
                  method       = "GET"
                  url_template = "x"
                  request = [
                    {
                      header = [
                        {
                          name = "Authorization"
                          required = true
                        },
                      ]
                    },
                  ]
                  response = [
                    {
                      status_code = 200
                    },
                  ]
                },
              ]
            }
            """);

        var op = parsed.ApiGroups.Single().Operations.Single();
        Assert.NotNull(op.RequestArray);
        Assert.Single(op.RequestArray!.Items);
        Assert.NotNull(op.ResponsesArray);
        Assert.Single(op.ResponsesArray!.Items);
    }
}
