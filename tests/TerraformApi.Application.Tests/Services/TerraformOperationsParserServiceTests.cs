using TerraformApi.Application.Services;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="TerraformOperationsParserService"/> covering:
/// - API info extraction from the api = [{ ... }] block
/// - Operation block extraction from api_operations
/// - Individual operation field parsing (method, url_template, operation_id, etc.)
/// - Path parameter inference from url_template placeholders
/// - Request parameter extraction (header, query_parameter)
/// - Request body content type extraction from representations
/// - Response code extraction from response sub-blocks
/// - Edge cases: empty input, no operations, missing fields
/// </summary>
public class TerraformOperationsParserServiceTests
{
    private readonly TerraformOperationsParserService _parser = new();

    /// <summary>
    /// Builds a realistic Terraform HCL block with the standard APIM structure.
    /// </summary>
    private static string BuildFullTerraform(params string[] operationBlocks)
    {
        var ops = string.Join("\n", operationBlocks);
        return $$"""
            test-api-group = {
              product = []
              api = [
                {
                    apim_resource_group_name         = "rg-apim-dev"
                    apim_name                        = "apim-company-dev"
                    name                             = "test-api-dev"
                    display_name                     = "Test API - dev"
                    path                             = "myapp.dev/v1/api"
                    service_url                      = "https://api-dev.company.com/my-service/"
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

    private static string SimpleOperation(string opId, string method, string urlTemplate) => $$"""
            {
                operation_id             = "{{opId}}"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "{{method}} {{urlTemplate}}"
                method                   = "{{method}}"
                url_template             = "{{urlTemplate}}"
                status_code              = "200"
                description              = ""
            },
        """;

    private static string OperationWithRequestParams() => """
            {
                operation_id             = "get-users-dev"
                apim_resource_group_name = "rg-apim-dev"
                apim_name                = "apim-company-dev"
                api_name                 = "test-api-dev"
                display_name             = "Get Users"
                method                   = "GET"
                url_template             = "users/{userId}"
                status_code              = "200"
                description              = "Returns a list of users"

                request = [
                  {
                    header = [
                      {
                        name        = "Authorization"
                        required    = true
                        type        = "string"
                        description = "Bearer token"
                      },
                      {
                        name        = "X-Request-Id"
                        required    = false
                        type        = "string"
                        description = "Correlation ID"
                      }
                    ]
                    query_parameter = [
                      {
                        name        = "limit"
                        required    = false
                        type        = "integer"
                        description = "Maximum number of results"
                      },
                      {
                        name        = "offset"
                        required    = false
                        type        = "integer"
                        description = "Pagination offset"
                      }
                    ]
                    representation = [
                      {
                        content_type = "application/json"
                      }
                    ]
                  }
                ]

                response = [
                  {
                    status_code  = 200
                    description  = "OK"
                    representation = [
                      {
                        content_type = "application/json"
                      }
                    ]
                  },
                  {
                    status_code  = 404
                    description  = "Not Found"
                  }
                ]
            },
        """;

    // ── Full parse ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyInput_ReturnsFailure()
    {
        var result = _parser.Parse("");
        Assert.False(result.Success);
        Assert.Contains("required", result.Error!);
    }

    [Fact]
    public void Parse_NullInput_ReturnsFailure()
    {
        var result = _parser.Parse(null!);
        Assert.False(result.Success);
    }

    [Fact]
    public void Parse_NoOperations_ReturnsZeroOperations()
    {
        var terraform = BuildFullTerraform();
        var result = _parser.Parse(terraform);

        Assert.True(result.Success);
        Assert.Equal(0, result.TotalOperations);
        Assert.Empty(result.Operations);
    }

    [Fact]
    public void Parse_SingleOperation_ReturnsOne()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var result = _parser.Parse(terraform);

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalOperations);
        Assert.Single(result.Operations);
    }

    [Fact]
    public void Parse_MultipleOperations_ReturnsAll()
    {
        var terraform = BuildFullTerraform(
            SimpleOperation("get-users-dev", "GET", "users"),
            SimpleOperation("create-user-dev", "POST", "users"),
            SimpleOperation("delete-user-dev", "DELETE", "users/{id}"));

        var result = _parser.Parse(terraform);

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalOperations);
    }

    // ── API info extraction ─────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsApiInfo()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var result = _parser.Parse(terraform);

        Assert.NotNull(result.Api);
        Assert.Equal("Test API - dev", result.Api.Title);
        Assert.Equal("test-api-dev", result.Api.Name);
        Assert.Equal("myapp.dev/v1/api", result.Api.Path);
        Assert.Equal("https://api-dev.company.com/my-service/", result.Api.ServiceUrl);
        Assert.Equal("dev", result.Api.Environment);
        Assert.Equal("terraform", result.Api.Source);
    }

    // ── Operation field parsing ─────────────────────────────────────────

    [Fact]
    public void Parse_OperationHasCorrectMethod()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("GET", op.Method);
    }

    [Fact]
    public void Parse_OperationHasCorrectUrlTemplate()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("users", op.UrlTemplate);
    }

    [Fact]
    public void Parse_OperationHasCorrectPath()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("/users", op.Path);
    }

    [Fact]
    public void Parse_OperationHasOperationId()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("get-users-dev", op.OperationId);
    }

    [Fact]
    public void Parse_OperationUsesDisplayNameAsDescription()
    {
        // When description is empty, display_name is used
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("GET users", op.Description);
    }

    [Fact]
    public void Parse_OperationPrefersDescriptionOverDisplayName()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        Assert.Equal("Returns a list of users", op.Description);
    }

    [Fact]
    public void Parse_OperationHasStatusCodeInResponseCodes()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-users-dev", "GET", "users"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.ResponseCodes);
        Assert.Contains(200, op.ResponseCodes);
    }

    // ── Path parameter extraction ───────────────────────────────────────

    [Fact]
    public void Parse_PathParameters_ExtractedFromUrlTemplate()
    {
        var terraform = BuildFullTerraform(SimpleOperation("get-user-dev", "GET", "users/{userId}"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.Parameters);
        var pathParam = op.Parameters.First(p => p.In == "path");
        Assert.Equal("userId", pathParam.Name);
        Assert.True(pathParam.Required);
        Assert.Equal("string", pathParam.Type);
    }

    [Fact]
    public void Parse_MultiplePathParameters_AllExtracted()
    {
        var terraform = BuildFullTerraform(
            SimpleOperation("get-profile-dev", "GET", "users/{userId}/profiles/{profileId}"));
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.Parameters);
        var pathParams = op.Parameters.Where(p => p.In == "path").ToList();
        Assert.Equal(2, pathParams.Count);
        Assert.Contains(pathParams, p => p.Name == "userId");
        Assert.Contains(pathParams, p => p.Name == "profileId");
    }

    // ── Request parameter extraction (header + query) ───────────────────

    [Fact]
    public void Parse_HeaderParameters_Extracted()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.Parameters);
        var headers = op.Parameters.Where(p => p.In == "header").ToList();
        Assert.Equal(2, headers.Count);
        Assert.Contains(headers, h => h.Name == "Authorization" && h.Required);
        Assert.Contains(headers, h => h.Name == "X-Request-Id" && !h.Required);
    }

    [Fact]
    public void Parse_QueryParameters_Extracted()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.Parameters);
        var queryParams = op.Parameters.Where(p => p.In == "query").ToList();
        Assert.Equal(2, queryParams.Count);
        Assert.Contains(queryParams, q => q.Name == "limit" && q.Type == "integer");
        Assert.Contains(queryParams, q => q.Name == "offset" && q.Type == "integer");
    }

    [Fact]
    public void Parse_ParameterDescriptions_Preserved()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        var authHeader = op.Parameters!.First(p => p.Name == "Authorization");
        Assert.Equal("Bearer token", authHeader.Description);
    }

    // ── Request body content types ──────────────────────────────────────

    [Fact]
    public void Parse_RequestBodyContentTypes_Extracted()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.RequestBodyContentTypes);
        Assert.Contains("application/json", op.RequestBodyContentTypes);
    }

    // ── Response codes ──────────────────────────────────────────────────

    [Fact]
    public void Parse_ResponseCodes_FromResponseBlocks()
    {
        var terraform = BuildFullTerraform(OperationWithRequestParams());
        var op = _parser.Parse(terraform).Operations[0];

        Assert.NotNull(op.ResponseCodes);
        Assert.Contains(200, op.ResponseCodes);
        Assert.Contains(404, op.ResponseCodes);
    }

    // ── Internal method tests ───────────────────────────────────────────

    [Fact]
    public void ExtractApiInfo_ReturnsCorrectFields()
    {
        var terraform = BuildFullTerraform();
        var info = TerraformOperationsParserService.ExtractApiInfo(terraform);

        Assert.Equal("Test API - dev", info.Title);
        Assert.Equal("test-api-dev", info.Name);
    }

    [Fact]
    public void ExtractOperationBlocks_ReturnsCorrectCount()
    {
        var terraform = BuildFullTerraform(
            SimpleOperation("op1", "GET", "a"),
            SimpleOperation("op2", "POST", "b"));

        var blocks = TerraformOperationsParserService.ExtractOperationBlocks(terraform);
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void ExtractOperationBlocks_BracesInQuotedStrings_Handled()
    {
        var terraform = """
            api_operations = [
              {
                  operation_id = "test-op"
                  method       = "POST"
                  url_template = "webhook"
                  status_code  = "200"
                  description  = "Payload: {\"key\": \"value\"}"
                  display_name = "Webhook"
              },
            ]
            """;

        var blocks = TerraformOperationsParserService.ExtractOperationBlocks(terraform);
        Assert.Single(blocks);
        Assert.Contains("test-op", blocks[0]);
    }

    [Fact]
    public void ParseOperationBlock_MissingMethod_ReturnsNull()
    {
        var block = """
            {
                url_template = "users"
                status_code  = "200"
            }
            """;

        Assert.Null(TerraformOperationsParserService.ParseOperationBlock(block));
    }

    [Fact]
    public void ParseOperationBlock_MissingUrlTemplate_ReturnsNull()
    {
        var block = """
            {
                method      = "GET"
                status_code = "200"
            }
            """;

        Assert.Null(TerraformOperationsParserService.ParseOperationBlock(block));
    }

    [Fact]
    public void ExtractFieldValue_StandardField_ReturnsValue()
    {
        var text = """method = "GET" """;
        Assert.Equal("GET", TerraformOperationsParserService.ExtractFieldValue(text, "method"));
    }

    [Fact]
    public void ExtractFieldValue_AlignedWhitespace_ReturnsValue()
    {
        var text = """method                   = "POST" """;
        Assert.Equal("POST", TerraformOperationsParserService.ExtractFieldValue(text, "method"));
    }

    [Fact]
    public void ExtractFieldValue_Missing_ReturnsNull()
    {
        var text = """method = "GET" """;
        Assert.Null(TerraformOperationsParserService.ExtractFieldValue(text, "nonexistent"));
    }

    [Fact]
    public void ExtractResponseCodes_FromResponseSection()
    {
        var block = """
            response = [
              {
                status_code = 200
                description = "OK"
              },
              {
                status_code = 400
                description = "Bad Request"
              }
            ]
            """;

        var codes = TerraformOperationsParserService.ExtractResponseCodes(block);
        Assert.Equal(2, codes.Count);
        Assert.Contains(200, codes);
        Assert.Contains(400, codes);
    }

    [Fact]
    public void ExtractRequestParameters_Headers_Extracted()
    {
        var block = """
            request = [
              {
                header = [
                  {
                    name        = "Authorization"
                    required    = true
                    type        = "string"
                    description = "Bearer token"
                  }
                ]
              }
            ]
            """;

        var headers = TerraformOperationsParserService.ExtractRequestParameters(block, "header");
        Assert.Single(headers);
        Assert.Equal("Authorization", headers[0].Name);
        Assert.Equal("header", headers[0].In);
        Assert.True(headers[0].Required);
    }

    [Fact]
    public void ExtractRequestParameters_QueryParams_Extracted()
    {
        var block = """
            request = [
              {
                query_parameter = [
                  {
                    name        = "search"
                    required    = false
                    type        = "string"
                    description = "Search term"
                  }
                ]
              }
            ]
            """;

        var queryParams = TerraformOperationsParserService.ExtractRequestParameters(block, "query_parameter");
        Assert.Single(queryParams);
        Assert.Equal("search", queryParams[0].Name);
        Assert.Equal("query", queryParams[0].In);
    }
}
