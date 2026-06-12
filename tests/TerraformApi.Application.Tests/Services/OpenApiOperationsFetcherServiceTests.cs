using TerraformApi.Application.Services;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="OpenApiOperationsFetcherService"/> covering:
/// - Successful parsing of valid OpenAPI 3.0 JSON
/// - API info extraction (title, version, description, sourceUrl)
/// - Operation extraction (method, urlTemplate, path, operationId, description)
/// - Parameter extraction (path, query, header) with type mapping
/// - Request body content type extraction
/// - Response code extraction
/// - Tag extraction
/// - Edge cases: empty input, invalid JSON, no paths, missing fields
/// - Schema type mapping (integer, int64, boolean, number, array, object)
/// </summary>
public class OpenApiOperationsFetcherServiceTests
{
    private readonly OpenApiOperationsFetcherService _fetcher = new();

    #region Helpers

    private static string BuildOpenApiSpec(
        string title = "Test API",
        string version = "1.0.0",
        string? description = null,
        string paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "summary": "Get Users",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """)
    {
        var descPart = description != null ? $"""
            , "description": "{description}"
            """ : "";

        return $$"""
            {
              "openapi": "3.0.1",
              "info": { "title": "{{title}}", "version": "{{version}}"{{descPart}} },
              "paths": { {{paths}} }
            }
            """;
    }

    #endregion

    #region Error cases

    [Fact]
    public void ParseOperations_EmptyString_ReturnsFailure()
    {
        var result = _fetcher.ParseOperations("");
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseOperations_InvalidJson_ReturnsFailure()
    {
        var result = _fetcher.ParseOperations("not json at all {{{");
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseOperations_ValidJsonNoPaths_ReturnsFailure()
    {
        var result = _fetcher.ParseOperations("""{ "openapi": "3.0.1", "info": { "title": "T", "version": "1" }, "paths": {} }""");
        Assert.False(result.Success);
        Assert.Contains("No API paths found", result.Error!);
    }

    [Fact]
    public void ParseOperations_EmptyJsonObject_ReturnsFailure()
    {
        var result = _fetcher.ParseOperations("{}");
        Assert.False(result.Success);
    }

    #endregion

    #region API info

    [Fact]
    public void ParseOperations_ExtractsApiTitle()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(title: "My Cool API"));
        Assert.True(result.Success);
        Assert.Equal("My Cool API", result.Api!.Title);
    }

    [Fact]
    public void ParseOperations_ExtractsApiVersion()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(version: "2.5.0"));
        Assert.True(result.Success);
        Assert.Equal("2.5.0", result.Api!.Version);
    }

    [Fact]
    public void ParseOperations_ExtractsApiDescription()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(description: "A great API"));
        Assert.True(result.Success);
        Assert.Equal("A great API", result.Api!.Description);
    }

    [Fact]
    public void ParseOperations_NullDescriptionWhenEmpty()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Null(result.Api!.Description);
    }

    [Fact]
    public void ParseOperations_SourceUrlDefaultsToInline()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("inline", result.Api!.SourceUrl);
    }

    [Fact]
    public void ParseOperations_SourceUrlFromParameter()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(), "https://example.com/api.json");
        Assert.True(result.Success);
        Assert.Equal("https://example.com/api.json", result.Api!.SourceUrl);
    }

    [Fact]
    public void ParseOperations_SourceIsOpenApi()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("openapi", result.Api!.Source);
    }

    [Fact]
    public void ParseOperations_TerraformFieldsAreNull()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Null(result.Api!.Name);
        Assert.Null(result.Api.Path);
        Assert.Null(result.Api.ServiceUrl);
        Assert.Null(result.Api.Environment);
    }

    #endregion

    #region Operations - basic fields

    [Fact]
    public void ParseOperations_ExtractsOperationMethod()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("GET", result.Operations[0].Method);
    }

    [Fact]
    public void ParseOperations_ExtractsUrlTemplate()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("users", result.Operations[0].UrlTemplate);
    }

    [Fact]
    public void ParseOperations_ExtractsPath()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("/users", result.Operations[0].Path);
    }

    [Fact]
    public void ParseOperations_ExtractsOperationId()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("getUsers", result.Operations[0].OperationId);
    }

    [Fact]
    public void ParseOperations_DescriptionFromSummary()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Equal("Get Users", result.Operations[0].Description);
    }

    [Fact]
    public void ParseOperations_DescriptionFallsBackToDescription()
    {
        var paths = """
            "/items": {
              "get": {
                "operationId": "getItems",
                "description": "Returns all items",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.True(result.Success);
        Assert.Equal("Returns all items", result.Operations[0].Description);
    }

    [Fact]
    public void ParseOperations_NullDescriptionWhenBothMissing()
    {
        var paths = """
            "/items": {
              "get": {
                "operationId": "getItems",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.True(result.Success);
        Assert.Null(result.Operations[0].Description);
    }

    [Fact]
    public void ParseOperations_TotalOperationsCount()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "operationId": "createUser",
                "responses": { "201": { "description": "Created" } }
              }
            },
            "/items": {
              "get": {
                "operationId": "getItems",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.True(result.Success);
        Assert.Equal(3, result.TotalOperations);
        Assert.Equal(3, result.Operations.Count);
    }

    #endregion

    #region Tags

    [Fact]
    public void ParseOperations_ExtractsTags()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "tags": ["users", "admin"],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.True(result.Success);
        Assert.Equal(["users", "admin"], result.Operations[0].Tags);
    }

    [Fact]
    public void ParseOperations_EmptyOrNullTagsWhenMissing()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        // OpenAPI reader may return empty list rather than null when no tags specified
        Assert.True(result.Operations[0].Tags is null or { Count: 0 });
    }

    #endregion

    #region Parameters

    [Fact]
    public void ParseOperations_ExtractsPathParameter()
    {
        var paths = """
            "/users/{userId}": {
              "get": {
                "operationId": "getUserById",
                "parameters": [
                  { "name": "userId", "in": "path", "required": true, "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        var param = result.Operations[0].Parameters![0];

        Assert.Equal("userId", param.Name);
        Assert.Equal("path", param.In);
        Assert.True(param.Required);
        Assert.Equal("string", param.Type);
    }

    [Fact]
    public void ParseOperations_ExtractsQueryParameter()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "limit", "in": "query", "required": false, "schema": { "type": "integer" }, "description": "Max results" }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        var param = result.Operations[0].Parameters![0];

        Assert.Equal("limit", param.Name);
        Assert.Equal("query", param.In);
        Assert.False(param.Required);
        Assert.Equal("integer", param.Type);
        Assert.Equal("Max results", param.Description);
    }

    [Fact]
    public void ParseOperations_ExtractsHeaderParameter()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "X-Api-Key", "in": "header", "required": true, "schema": { "type": "string" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        var param = result.Operations[0].Parameters![0];

        Assert.Equal("X-Api-Key", param.Name);
        Assert.Equal("header", param.In);
        Assert.True(param.Required);
    }

    [Fact]
    public void ParseOperations_NullParametersWhenNone()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Null(result.Operations[0].Parameters);
    }

    [Fact]
    public void ParseOperations_PathLevelParametersIncluded()
    {
        var paths = """
            "/users/{userId}": {
              "parameters": [
                { "name": "userId", "in": "path", "required": true, "schema": { "type": "string" } }
              ],
              "get": {
                "operationId": "getUserById",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Single(result.Operations[0].Parameters!);
        Assert.Equal("userId", result.Operations[0].Parameters![0].Name);
    }

    [Fact]
    public void ParseOperations_OperationParameterOverridesPathLevel()
    {
        var paths = """
            "/users/{userId}": {
              "parameters": [
                { "name": "userId", "in": "path", "required": true, "schema": { "type": "string" }, "description": "From path level" }
              ],
              "get": {
                "operationId": "getUserById",
                "parameters": [
                  { "name": "userId", "in": "path", "required": true, "schema": { "type": "integer" }, "description": "From operation" }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        // Should be deduplicated — operation-level wins
        Assert.Single(result.Operations[0].Parameters!);
        Assert.Equal("integer", result.Operations[0].Parameters![0].Type);
        Assert.Equal("From operation", result.Operations[0].Parameters![0].Description);
    }

    #endregion

    #region Request body

    [Fact]
    public void ParseOperations_ExtractsRequestBodyContentTypes()
    {
        var paths = """
            "/users": {
              "post": {
                "operationId": "createUser",
                "requestBody": {
                  "content": {
                    "application/json": { "schema": { "type": "object" } },
                    "application/xml": { "schema": { "type": "object" } }
                  }
                },
                "responses": { "201": { "description": "Created" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Contains("application/json", result.Operations[0].RequestBodyContentTypes!);
        Assert.Contains("application/xml", result.Operations[0].RequestBodyContentTypes!);
    }

    [Fact]
    public void ParseOperations_NullRequestBodyWhenNone()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.Null(result.Operations[0].RequestBodyContentTypes);
    }

    #endregion

    #region Response codes

    [Fact]
    public void ParseOperations_ExtractsResponseCodes()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "responses": {
                  "200": { "description": "OK" },
                  "400": { "description": "Bad Request" },
                  "404": { "description": "Not Found" }
                }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Contains(200, result.Operations[0].ResponseCodes!);
        Assert.Contains(400, result.Operations[0].ResponseCodes!);
        Assert.Contains(404, result.Operations[0].ResponseCodes!);
    }

    [Fact]
    public void ParseOperations_IgnoresNonNumericResponseCodes()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "responses": {
                  "200": { "description": "OK" },
                  "default": { "description": "Error" }
                }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Single(result.Operations[0].ResponseCodes!);
        Assert.Equal(200, result.Operations[0].ResponseCodes![0]);
    }

    #endregion

    #region Schema type mapping

    [Fact]
    public void ParseOperations_MapsInt64Type()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "id", "in": "query", "schema": { "type": "integer", "format": "int64" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("int64", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_MapsBooleanType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "active", "in": "query", "schema": { "type": "boolean" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("boolean", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_MapsNumberType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "score", "in": "query", "schema": { "type": "number" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("number", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_MapsDoubleType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "score", "in": "query", "schema": { "type": "number", "format": "double" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("double", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_MapsArrayType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "ids", "in": "query", "schema": { "type": "array", "items": { "type": "integer" } } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("array<integer>", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_MapsObjectType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "filter", "in": "query", "schema": { "type": "object" } }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("object", result.Operations[0].Parameters![0].Type);
    }

    [Fact]
    public void ParseOperations_DefaultsToStringType()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "parameters": [
                  { "name": "name", "in": "query" }
                ],
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal("string", result.Operations[0].Parameters![0].Type);
    }

    #endregion

    #region Multiple methods on same path

    [Fact]
    public void ParseOperations_MultipleMethodsOnSamePath()
    {
        var paths = """
            "/users": {
              "get": {
                "operationId": "getUsers",
                "responses": { "200": { "description": "OK" } }
              },
              "post": {
                "operationId": "createUser",
                "requestBody": { "content": { "application/json": { "schema": { "type": "object" } } } },
                "responses": { "201": { "description": "Created" } }
              },
              "delete": {
                "operationId": "deleteUsers",
                "responses": { "204": { "description": "No Content" } }
              }
            }
            """;
        var result = _fetcher.ParseOperations(BuildOpenApiSpec(paths: paths));
        Assert.Equal(3, result.TotalOperations);

        var methods = result.Operations.Select(o => o.Method).OrderBy(m => m).ToList();
        Assert.Contains("DELETE", methods);
        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    #endregion

    #region Unified output format

    [Fact]
    public void ParseOperations_ResultIsSuccessful()
    {
        var result = _fetcher.ParseOperations(BuildOpenApiSpec());
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Api);
        Assert.NotEmpty(result.Operations);
    }

    [Fact]
    public void ParseOperations_FailureHasNullApiAndEmptyOps()
    {
        var result = _fetcher.ParseOperations("");
        Assert.False(result.Success);
        Assert.Null(result.Api);
        Assert.Empty(result.Operations);
    }

    #endregion
}
