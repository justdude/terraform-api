using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>Matcher tests M1–M9 (§5.4) plus normalization and signature tests.</summary>
public class OperationMatcherServiceTests
{
    private readonly OperationMatcherService _matcher = new(new TerraformInterpolationResolver());
    private readonly ApimTerraformReaderService _reader = new(new HclParserService());

    private static ApimApiOperation OpenApiOp(
        string operationId, string method, string url) => new()
    {
        OperationId = operationId,
        ApimResourceGroupName = "rg",
        ApimName = "apim",
        ApiName = "my-api",
        DisplayName = operationId,
        Method = method,
        UrlTemplate = url
    };

    private ParsedApiOperation TerraformOp(string operationId, string method, string url)
    {
        var parsed = _reader.Read($$"""
            g = {
              api_operations = [
                {
                  operation_id = "{{operationId}}"
                  method       = "{{method}}"
                  url_template = "{{url}}"
                  apim_resource_group_name = "rg"
                  api_name     = "my-api"
                },
              ]
            }
            """);
        return parsed.ApiGroups.Single().Operations.Single();
    }

    // M1
    [Fact]
    public void Match_SameOperationIdMethodUrl_Matched()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("getUsers", "GET", "/users"), strategy);
        var tf = _matcher.FingerprintFromTerraform(TerraformOp("getUsers", "GET", "users"), strategy);

        var result = _matcher.Match([openApi], [tf], strategy);

        Assert.Single(result.Matched);
        Assert.Empty(result.OnlyInOpenApi);
        Assert.Empty(result.OnlyInTerraform);
    }

    // M2
    [Fact]
    public void Match_DifferentOperationIds_MatchedByMethodAndUrl()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("listUsers", "GET", "/users"), strategy);
        var tf = _matcher.FingerprintFromTerraform(TerraformOp("${prefix}-list-${env}", "GET", "users"), strategy);

        var result = _matcher.Match([openApi], [tf], strategy);

        Assert.Single(result.Matched);
    }

    // M3
    [Fact]
    public void Match_DifferentCase_NoMatchByDefault_MatchWithIgnoreCase()
    {
        var strict = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("getUsers", "GET", "/users"), strict);
        var tf = _matcher.FingerprintFromTerraform(TerraformOp("other-id", "GET", "/Users"), strict);

        var strictResult = _matcher.Match([openApi], [tf], strict);
        Assert.Empty(strictResult.Matched);

        var relaxed = new OperationMatchStrategy
        {
            UrlNormalization = new UrlNormalizationOptions { IgnoreCase = true }
        };
        var openApiRelaxed = _matcher.FingerprintFromOpenApi(OpenApiOp("getUsers", "GET", "/users"), relaxed);
        var tfRelaxed = _matcher.FingerprintFromTerraform(TerraformOp("other-id", "GET", "/Users"), relaxed);

        var relaxedResult = _matcher.Match([openApiRelaxed], [tfRelaxed], relaxed);
        Assert.Single(relaxedResult.Matched);
    }

    // M4
    [Fact]
    public void Match_DifferentParamNames_NoMatch()
    {
        // {id} vs {userId}: parameter names matter (NormalizeBraceParams only
        // unifies syntax, not names).
        var strategy = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("getUser", "GET", "/users/{id}"), strategy);
        var tf = _matcher.FingerprintFromTerraform(TerraformOp("other", "GET", "/users/{userId}"), strategy);

        var result = _matcher.Match([openApi], [tf], strategy);
        Assert.Empty(result.Matched);
    }

    [Fact]
    public void Match_ColonParamSyntax_UnifiedWithBraces()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("getUser", "GET", "/users/{id}"), strategy);
        var tf = _matcher.FingerprintFromTerraform(TerraformOp("other", "GET", "/users/:id"), strategy);

        var result = _matcher.Match([openApi], [tf], strategy);
        Assert.Single(result.Matched);
    }

    // M5
    [Fact]
    public void Match_SameMethodUrlDifferentParams_AmbiguityOrParamsKey()
    {
        var strategy = new OperationMatchStrategy
        {
            Keys = [OperationMatchKey.MethodAndUrlAndParams]
        };

        var op1 = OpenApiOp("searchByName", "GET", "/search") with
        {
            Requests = [new ApimOperationRequest { QueryParameters = [new ApimParameter { Name = "name" }] }]
        };
        var op2 = OpenApiOp("searchByTag", "GET", "/search") with
        {
            Requests = [new ApimOperationRequest { QueryParameters = [new ApimParameter { Name = "tag" }] }]
        };

        var fp1 = _matcher.FingerprintFromOpenApi(op1, strategy);
        var fp2 = _matcher.FingerprintFromOpenApi(op2, strategy);

        // Different query params → different fingerprints under MethodAndUrlAndParams.
        Assert.NotEqual(
            OperationMatcherService.KeyValue(fp1, OperationMatchKey.MethodAndUrlAndParams, strategy, fp1),
            OperationMatcherService.KeyValue(fp2, OperationMatchKey.MethodAndUrlAndParams, strategy, fp2));

        // Under plain MethodAndUrl with two identical TF candidates → ambiguity.
        var plain = new OperationMatchStrategy { Keys = [OperationMatchKey.MethodAndUrl] };
        var tf1 = _matcher.FingerprintFromTerraform(TerraformOp("a", "GET", "/search"), plain);
        var tf2 = _matcher.FingerprintFromTerraform(TerraformOp("b", "GET", "/search"), plain);
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("search", "GET", "/search"), plain);

        var result = _matcher.Match([openApi], [tf1, tf2], plain);

        Assert.Empty(result.Matched);
        Assert.Single(result.Ambiguities);
        Assert.Empty(result.OnlyInOpenApi); // ambiguous — not safe to auto-add
    }

    // M6
    [Fact]
    public void Match_FullyInterpolatedUrl_OnlyMatchesInResolvedMode()
    {
        // Structural mode: "${operation_path}" ≠ "/users/{id}" → no match.
        var structural = new OperationMatchStrategy { TryResolvedComparisonAsFallback = false };
        var openApiS = _matcher.FingerprintFromOpenApi(OpenApiOp("getUser", "GET", "/users/{id}"), structural);
        var tfS = _matcher.FingerprintFromTerraform(TerraformOp("${operation_prefix}-${env}", "GET", "${operation_path}"), structural);

        var structuralResult = _matcher.Match([openApiS], [tfS], structural);
        Assert.Empty(structuralResult.Matched);
        Assert.Single(structuralResult.OnlyInTerraform);
        Assert.Single(structuralResult.OnlyInOpenApi);

        // Resolved mode: operation_path = users/{id} → match.
        var resolved = new OperationMatchStrategy
        {
            TryResolvedComparisonAsFallback = true,
            VariableContext = new Dictionary<string, string> { ["operation_path"] = "users/{id}" }
        };
        var openApiR = _matcher.FingerprintFromOpenApi(OpenApiOp("getUser", "GET", "/users/{id}"), resolved);
        var tfR = _matcher.FingerprintFromTerraform(TerraformOp("${operation_prefix}-${env}", "GET", "${operation_path}"), resolved);

        var resolvedResult = _matcher.Match([openApiR], [tfR], resolved);
        Assert.Single(resolvedResult.Matched);
    }

    // M7
    [Fact]
    public void Match_ThreeAndThree_AllMatched()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = new[]
        {
            _matcher.FingerprintFromOpenApi(OpenApiOp("a", "GET", "/a"), strategy),
            _matcher.FingerprintFromOpenApi(OpenApiOp("b", "POST", "/b"), strategy),
            _matcher.FingerprintFromOpenApi(OpenApiOp("c", "DELETE", "/c"), strategy)
        };
        var tf = new[]
        {
            _matcher.FingerprintFromTerraform(TerraformOp("x", "DELETE", "c"), strategy),
            _matcher.FingerprintFromTerraform(TerraformOp("y", "GET", "a"), strategy),
            _matcher.FingerprintFromTerraform(TerraformOp("z", "POST", "b"), strategy)
        };

        var result = _matcher.Match(openApi, tf, strategy);

        Assert.Equal(3, result.Matched.Count);
        Assert.Empty(result.OnlyInOpenApi);
        Assert.Empty(result.OnlyInTerraform);
    }

    // M8
    [Fact]
    public void Match_OpenApiEmpty_AllTfPreserved()
    {
        var strategy = new OperationMatchStrategy();
        var tf = new[]
        {
            _matcher.FingerprintFromTerraform(TerraformOp("a", "GET", "/a"), strategy),
            _matcher.FingerprintFromTerraform(TerraformOp("b", "POST", "/b"), strategy)
        };

        var result = _matcher.Match([], tf, strategy);

        Assert.Empty(result.Matched);
        Assert.Equal(2, result.OnlyInTerraform.Count);
    }

    // M9
    [Fact]
    public void Match_TfEmpty_AllOpenApiAdded()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = new[]
        {
            _matcher.FingerprintFromOpenApi(OpenApiOp("a", "GET", "/a"), strategy),
            _matcher.FingerprintFromOpenApi(OpenApiOp("b", "POST", "/b"), strategy)
        };

        var result = _matcher.Match(openApi, [], strategy);

        Assert.Empty(result.Matched);
        Assert.Equal(2, result.OnlyInOpenApi.Count);
    }

    [Fact]
    public void Match_ScopeKey_FiltersOtherApis()
    {
        var strategy = new OperationMatchStrategy();
        var openApi = _matcher.FingerprintFromOpenApi(OpenApiOp("get", "GET", "/x"), strategy);

        // TF operation belongs to a different API — out of scope.
        var tfOther = _matcher.FingerprintFromTerraform(TerraformOp("other", "GET", "/x"), strategy)
            with { ApiName = "other-api", ApiResourceGroup = "other-rg" };

        var scope = new ApimApiGroupKey { ApimResourceGroupNameRaw = "rg", ApiNameRaw = "my-api" };
        var result = _matcher.Match([openApi], [tfOther], strategy, scope);

        Assert.Empty(result.Matched);
        Assert.Single(result.OnlyInOpenApi);
        Assert.Empty(result.OnlyInTerraform); // out-of-scope ops are excluded entirely
    }

    [Fact]
    public void NormalizeUrl_AppliesAllDefaultRules()
    {
        var options = new UrlNormalizationOptions();

        Assert.Equal("users", OperationMatcherService.NormalizeUrl("/users/", options));
        Assert.Equal("users/{id}", OperationMatcherService.NormalizeUrl("/users//{id}", options));
        Assert.Equal("users/{id}", OperationMatcherService.NormalizeUrl("users/:id", options));
        Assert.Equal("https://x/users", OperationMatcherService.NormalizeUrl("HTTPS://x/users", options));
    }

    [Fact]
    public void ParameterSignature_SortedCaseInsensitive()
    {
        var strategy = new OperationMatchStrategy();
        var op = OpenApiOp("op", "GET", "/items/{ItemId}") with
        {
            Requests =
            [
                new ApimOperationRequest
                {
                    Headers = [new ApimParameter { Name = "Authorization" }],
                    QueryParameters = [new ApimParameter { Name = "Limit" }]
                }
            ]
        };

        var signature = OperationMatcherService.BuildParameterSignature(op, strategy);
        Assert.Equal("h:authorization|q:limit|t:itemid", signature);
    }
}
