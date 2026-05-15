using System.Net;
using System.Text.Json;
using TerraformApi.Mcp.Tools;

namespace TerraformApi.Mcp.Tests.Tools;

/// <summary>
/// Tests for the FetchOperationsTool MCP tool.
/// Uses ParseAndFormat (internal) for spec parsing tests, and FetchOperationsCore
/// with a mocked HttpClient for HTTP integration tests.
/// </summary>
public class FetchOperationsToolTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string ValidPetStoreSpec = """
        {
          "openapi": "3.0.1",
          "info": { "title": "Pet Store", "version": "1.0.0", "description": "A sample pet store API" },
          "paths": {
            "/pets": {
              "get": {
                "operationId": "listPets",
                "summary": "List all pets",
                "tags": ["pets"],
                "parameters": [
                  {
                    "name": "limit",
                    "in": "query",
                    "required": false,
                    "schema": { "type": "integer" },
                    "description": "Maximum number of pets to return"
                  }
                ],
                "responses": {
                  "200": { "description": "OK" },
                  "400": { "description": "Bad Request" }
                }
              },
              "post": {
                "operationId": "createPet",
                "summary": "Create a pet",
                "tags": ["pets"],
                "requestBody": {
                  "required": true,
                  "content": {
                    "application/json": {
                      "schema": { "type": "object" }
                    }
                  }
                },
                "responses": {
                  "201": { "description": "Created" }
                }
              }
            },
            "/pets/{petId}": {
              "get": {
                "operationId": "getPet",
                "summary": "Get a pet by ID",
                "tags": ["pets"],
                "parameters": [
                  {
                    "name": "petId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "200": { "description": "OK" },
                  "404": { "description": "Not Found" }
                }
              },
              "delete": {
                "operationId": "deletePet",
                "summary": "Delete a pet",
                "tags": ["pets", "admin"],
                "parameters": [
                  {
                    "name": "petId",
                    "in": "path",
                    "required": true,
                    "schema": { "type": "string" }
                  }
                ],
                "responses": {
                  "204": { "description": "Deleted" }
                }
              }
            }
          }
        }
        """;

    // ── ParseAndFormat (spec parsing) ────────────────────────────────

    [Fact]
    public void ParseAndFormat_ValidSpec_ReturnsAllOperations()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        Assert.Equal(4, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_ContainsApiInfo()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec, "https://example.com/swagger.json");
        using var doc = JsonDocument.Parse(result);

        var api = doc.RootElement.GetProperty("api");
        Assert.Equal("Pet Store", api.GetProperty("title").GetString());
        Assert.Equal("1.0.0", api.GetProperty("version").GetString());
        Assert.Equal("https://example.com/swagger.json", api.GetProperty("sourceUrl").GetString());
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_OperationsHaveCorrectMethods()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var methods = ops.EnumerateArray()
            .Select(o => o.GetProperty("method").GetString())
            .OrderBy(m => m)
            .ToList();

        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
        Assert.Contains("DELETE", methods);
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_OperationsHaveUrlTemplates()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var urls = ops.EnumerateArray()
            .Select(o => o.GetProperty("urlTemplate").GetString())
            .ToList();

        Assert.Contains("pets", urls);
        Assert.Contains("pets/{petId}", urls);
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_OperationsHaveOperationIds()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var ops = doc.RootElement.GetProperty("operations");
        var ids = ops.EnumerateArray()
            .Select(o => o.GetProperty("operationId").GetString())
            .ToList();

        Assert.Contains("listPets", ids);
        Assert.Contains("createPet", ids);
        Assert.Contains("getPet", ids);
        Assert.Contains("deletePet", ids);
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_ParametersIncluded()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var listPetsOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "listPets");

        var parameters = listPetsOp.GetProperty("parameters");
        Assert.Equal(1, parameters.GetArrayLength());

        var param = parameters[0];
        Assert.Equal("limit", param.GetProperty("name").GetString());
        Assert.Equal("query", param.GetProperty("in").GetString());
        Assert.Equal("integer", param.GetProperty("type").GetString());
        Assert.False(param.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_PathParametersIncluded()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var getPetOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "getPet");

        var parameters = getPetOp.GetProperty("parameters");
        var pathParam = parameters.EnumerateArray()
            .First(p => p.GetProperty("in").GetString() == "path");

        Assert.Equal("petId", pathParam.GetProperty("name").GetString());
        Assert.True(pathParam.GetProperty("required").GetBoolean());
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_TagsIncluded()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var deleteOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "deletePet");

        var tags = deleteOp.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString())
            .ToList();

        Assert.Contains("pets", tags);
        Assert.Contains("admin", tags);
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_RequestBodyTypesIncluded()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var createOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "createPet");

        var contentTypes = createOp.GetProperty("requestBodyContentTypes");
        Assert.Contains("application/json", contentTypes.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public void ParseAndFormat_ValidSpec_ResponseCodesIncluded()
    {
        var result = FetchOperationsTool.ParseAndFormat(ValidPetStoreSpec);
        using var doc = JsonDocument.Parse(result);

        var listOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "listPets");

        var codes = listOp.GetProperty("responseCodes").EnumerateArray()
            .Select(c => c.GetInt32())
            .ToList();

        Assert.Contains(200, codes);
        Assert.Contains(400, codes);
    }

    [Fact]
    public void ParseAndFormat_InvalidJson_ReturnsError()
    {
        var result = FetchOperationsTool.ParseAndFormat("{broken json!!");
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ParseAndFormat_EmptyPaths_ReturnsError()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Empty", "version": "1.0.0" },
              "paths": {}
            }
            """;

        var result = FetchOperationsTool.ParseAndFormat(spec);
        using var doc = JsonDocument.Parse(result);

        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ParseAndFormat_MinimalSpec_WorksWithDefaults()
    {
        var spec = """
            {
              "openapi": "3.0.1",
              "info": { "title": "Minimal", "version": "1.0.0" },
              "paths": {
                "/health": {
                  "get": {
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """;

        var result = FetchOperationsTool.ParseAndFormat(spec);
        using var doc = JsonDocument.Parse(result);

        Assert.Equal(1, doc.RootElement.GetProperty("totalOperations").GetInt32());

        var op = doc.RootElement.GetProperty("operations")[0];
        Assert.Equal("GET", op.GetProperty("method").GetString());
        Assert.Equal("health", op.GetProperty("urlTemplate").GetString());
    }

    // ── FetchOperationsCore (HTTP integration) ───────────────────────

    [Fact]
    public async Task FetchOperationsCore_EmptyUrl_ReturnsError()
    {
        using var client = new HttpClient();
        var result = await FetchOperationsTool.FetchOperationsCore(client, "");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("required", error.GetString());
    }

    [Fact]
    public async Task FetchOperationsCore_InvalidUrl_ReturnsError()
    {
        using var client = new HttpClient();
        var result = await FetchOperationsTool.FetchOperationsCore(client, "not-a-url");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Contains("Invalid URL", error.GetString());
    }

    [Fact]
    public async Task FetchOperationsCore_FtpScheme_ReturnsError()
    {
        using var client = new HttpClient();
        var result = await FetchOperationsTool.FetchOperationsCore(client, "ftp://example.com/spec.json");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task FetchOperationsCore_MockedHttpClient_ParsesResponse()
    {
        var handler = new MockHttpHandler(ValidPetStoreSpec);
        var client = new HttpClient(handler);

        var result = await FetchOperationsTool.FetchOperationsCore(client, "https://example.com/swagger.json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(4, doc.RootElement.GetProperty("totalOperations").GetInt32());
        Assert.Equal("https://example.com/swagger.json", doc.RootElement.GetProperty("api").GetProperty("sourceUrl").GetString());
    }

    [Fact]
    public async Task FetchOperationsCore_HttpError_ReturnsError()
    {
        var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
        var client = new HttpClient(handler);

        var result = await FetchOperationsTool.FetchOperationsCore(client, "https://example.com/swagger.json");

        using var doc = JsonDocument.Parse(result);
        Assert.True(doc.RootElement.TryGetProperty("error", out _));
    }

    /// <summary>
    /// Simple HTTP message handler that returns a canned response for testing.
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _statusCode;

        public MockHttpHandler(string responseBody = "", HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = responseBody;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
