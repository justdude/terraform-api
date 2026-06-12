using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the POST /api/fetch-operations endpoint.
/// Uses a custom WebApplicationFactory with a mocked HttpClientFactory
/// so the endpoint doesn't make real HTTP requests.
/// </summary>
public class FetchOperationsEndpointTests : IClassFixture<FetchOperationsEndpointTests.CustomFactory>
{
    private readonly HttpClient _client;

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
              }
            }
          }
        }
        """;

    public FetchOperationsEndpointTests(CustomFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FetchOperations_EmptyUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("required", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task FetchOperations_InvalidUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "not-a-url" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.Contains("Invalid URL", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task FetchOperations_FtpScheme_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "ftp://example.com/spec.json" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_Returns200WithOperations()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(3, doc.RootElement.GetProperty("totalOperations").GetInt32());
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_ReturnsApiInfo()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var api = doc.RootElement.GetProperty("api");
        Assert.Equal("Pet Store", api.GetProperty("title").GetString());
        Assert.Equal("1.0.0", api.GetProperty("version").GetString());
        Assert.Equal("https://mock-api.test/swagger.json", api.GetProperty("sourceUrl").GetString());
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_OperationsHaveCorrectFields()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var ops = doc.RootElement.GetProperty("operations");
        var methods = ops.EnumerateArray()
            .Select(o => o.GetProperty("method").GetString())
            .OrderBy(m => m)
            .ToList();

        Assert.Contains("GET", methods);
        Assert.Contains("POST", methods);
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_ParametersIncluded()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var listOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "listPets");

        var param = listOp.GetProperty("parameters")[0];
        Assert.Equal("limit", param.GetProperty("name").GetString());
        Assert.Equal("query", param.GetProperty("in").GetString());
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_TagsIncluded()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var listOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "listPets");

        var tags = listOp.GetProperty("tags").EnumerateArray()
            .Select(t => t.GetString())
            .ToList();

        Assert.Contains("pets", tags);
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_RequestBodyTypesIncluded()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var createOp = doc.RootElement.GetProperty("operations")
            .EnumerateArray()
            .First(o => o.GetProperty("operationId").GetString() == "createPet");

        var contentTypes = createOp.GetProperty("requestBodyContentTypes");
        Assert.Contains("application/json", contentTypes.EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task FetchOperations_ValidUrl_ResponseCodesIncluded()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/swagger.json" });

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

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
    public async Task FetchOperations_HttpError_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/fetch-operations",
            new { openApiUrl = "https://mock-api.test/not-found.json" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Failed to fetch", doc.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// Custom WebApplicationFactory that replaces the default IHttpClientFactory
    /// with one that returns a mocked HttpClient (no real network calls).
    /// </summary>
    public class CustomFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing IHttpClientFactory registrations
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IHttpClientFactory))
                    .ToList();
                foreach (var d in descriptors)
                    services.Remove(d);

                services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(ValidPetStoreSpec));
            });
        }
    }

    /// <summary>
    /// Mock IHttpClientFactory that returns an HttpClient backed by a canned handler.
    /// Returns 200 + pet store spec for /swagger.json, 404 for anything else.
    /// </summary>
    private sealed class MockHttpClientFactory : IHttpClientFactory
    {
        private readonly string _responseBody;

        public MockHttpClientFactory(string responseBody)
        {
            _responseBody = responseBody;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new MockHandler(_responseBody));
        }

        private sealed class MockHandler : HttpMessageHandler
        {
            private readonly string _body;

            public MockHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Return 404 for non-swagger URLs to test error path
                if (request.RequestUri?.AbsolutePath.Contains("not-found") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                    {
                        Content = new StringContent("Not found")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
