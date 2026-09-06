using System.Net;
using TerraformApi.Application.Services;

namespace TerraformApi.Application.Tests.Services;

/// <summary>
/// Tests for the shared OpenAPI source resolver used by both the API controllers
/// and the MCP tools, so both hosts accept the same inputs.
/// </summary>
public class OpenApiDocumentResolverTests
{
    private sealed class StubHandler(HttpStatusCode status, string content, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
    }

    private static HttpClient Client(string content, string mediaType = "text/plain", HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(status, content, mediaType));

    [Fact]
    public async Task ResolveAsync_InlineJson_ReturnedVerbatim()
    {
        const string json = "{ \"openapi\": \"3.0.3\" }";
        var result = await OpenApiDocumentResolver.ResolveAsync(Client(""), json, null);
        Assert.Equal(json, result);
    }

    [Fact]
    public async Task ResolveAsync_UrlYaml_IsAccepted()
    {
        // Regression: URL-fetched YAML was rejected as "not valid JSON" even
        // though the reader parses YAML and inline YAML already works.
        const string yaml = "openapi: 3.0.3\ninfo:\n  title: T\n  version: '1'\npaths: {}\n";
        using var client = Client(yaml, "application/yaml");

        var result = await OpenApiDocumentResolver.ResolveAsync(client, null, "https://example.com/spec.yaml");

        Assert.Equal(yaml, result);
    }

    [Fact]
    public async Task ResolveAsync_UrlJson_IsAccepted()
    {
        const string json = "{ \"openapi\": \"3.0.3\", \"paths\": {} }";
        using var client = Client(json, "application/json");

        var result = await OpenApiDocumentResolver.ResolveAsync(client, null, "https://example.com/spec.json");

        Assert.Equal(json, result);
    }

    [Fact]
    public async Task ResolveAsync_EmptyUrlResponse_Throws()
    {
        using var client = Client("   ");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenApiDocumentResolver.ResolveAsync(client, null, "https://example.com/spec"));
    }

    [Fact]
    public async Task ResolveAsync_NonHttpUrl_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenApiDocumentResolver.ResolveAsync(Client(""), null, "ftp://example.com/spec"));
    }

    [Fact]
    public async Task ResolveAsync_NeitherProvided_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OpenApiDocumentResolver.ResolveAsync(Client(""), null, null));
    }
}
