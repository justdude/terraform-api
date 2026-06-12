using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Guards the Swagger document and the SPA-fallback boundaries:
/// swagger.json must carry a valid OpenAPI version field, and /swagger + /api
/// URLs must never be answered with the frontend's index.html (a cached HTML
/// response renders as "definition does not specify a valid version field").
/// </summary>
public class SwaggerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SwaggerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerJson_HasValidOpenApiVersionField()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var version = doc.RootElement.GetProperty("openapi").GetString();

        Assert.NotNull(version);
        Assert.StartsWith("3.", version); // Swagger UI requires openapi: 3.x.y
    }

    [Fact]
    public async Task SwaggerJson_DocumentsAllApiPaths()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        string[] expected =
        [
            "/api/convert", "/api/convert/update", "/api/transform-environment",
            "/api/fetch-operations", "/api/parse-terraform-operations", "/api/validate",
            "/api/environments", "/api/health",
            "/api/sync", "/api/analyze-terraform", "/api/apply-template-profile",
            "/api/generate-product"
        ];
        foreach (var path in expected)
            Assert.Contains(path, paths);
    }

    [Fact]
    public async Task SwaggerUi_IsServed()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("swagger-ui", html);
    }

    [Fact]
    public async Task UnknownSwaggerPath_DoesNotFallBackToSpaHtml()
    {
        var response = await _client.GetAsync("/swagger/v1/does-not-exist.json");

        // Must be a clean 404 — never the SPA's index.html with a 200.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiPath_DoesNotFallBackToSpaHtml()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SpaFallback_StillServesFrontendWithNoStore()
    {
        var response = await _client.GetAsync("/some/client/route");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        // Fallback HTML must never be cacheable against arbitrary URLs.
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task MissingAsset_Returns404_NeverHtml()
    {
        // A missing stylesheet/script must 404 — serving index.html instead
        // makes the page render unstyled (browser discards HTML-as-CSS) and
        // can poison the browser cache for the asset URL.
        var response = await _client.GetAsync("/css/does-not-exist.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StylesCss_ServedWithCssContentType()
    {
        var response = await _client.GetAsync("/css/styles.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AppJs_ServedWithJsContentType()
    {
        var response = await _client.GetAsync("/js/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
