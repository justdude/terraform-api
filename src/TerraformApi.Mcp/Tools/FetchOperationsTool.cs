using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that fetches an OpenAPI specification from a URL and returns a structured
/// list of all operations with their methods, paths, parameters, and metadata.
/// Delegates parsing to <see cref="IOpenApiOperationsFetcher"/> (shared with the API endpoint).
/// </summary>
[McpServerToolType]
public static class FetchOperationsTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool(Name = "fetch_openapi_operations")]
    [Description("Fetches an OpenAPI/Swagger specification from a URL (e.g. https://example.com/swagger.json) " +
                 "and returns a structured list of all API operations. For each operation returns: " +
                 "HTTP method, URL path, operationId, parameters (name, type, location, required), " +
                 "description, and tags. Use this to inspect any API before converting to Terraform.")]
    public static async Task<string> FetchOperations(
        HttpClient httpClient,
        IOpenApiOperationsFetcher fetcher,
        [Description("URL to the OpenAPI/Swagger JSON endpoint (e.g. 'https://api.example.com/swagger/v1/swagger.json')")] string openApiUrl,
        CancellationToken cancellationToken = default)
    {
        return await FetchOperationsCore(httpClient, fetcher, openApiUrl, cancellationToken);
    }

    /// <summary>
    /// Core implementation separated for testability.
    /// </summary>
    internal static async Task<string> FetchOperationsCore(HttpClient httpClient, IOpenApiOperationsFetcher fetcher, string openApiUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(openApiUrl))
            return FormatError("OpenAPI URL is required.");

        if (!Uri.TryCreate(openApiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return FormatError($"Invalid URL: '{openApiUrl}'. Must be an absolute HTTP(S) URL.");

        string json;
        try
        {
            json = await httpClient.GetStringAsync(uri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return FormatError($"Failed to fetch OpenAPI spec from '{openApiUrl}': {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FormatError($"Request to '{openApiUrl}' timed out.");
        }

        return ParseAndFormat(fetcher, json, openApiUrl);
    }

    /// <summary>
    /// Parses an OpenAPI JSON string and formats the operations list.
    /// Delegates to <see cref="IOpenApiOperationsFetcher"/> for the actual parsing.
    /// </summary>
    internal static string ParseAndFormat(IOpenApiOperationsFetcher fetcher, string openApiJson, string sourceUrl = "inline")
    {
        var result = fetcher.ParseOperations(openApiJson, sourceUrl);

        if (!result.Success)
            return FormatError(result.Error ?? "Failed to parse OpenAPI document.");

        return JsonSerializer.Serialize(new
        {
            api = result.Api,
            totalOperations = result.TotalOperations,
            operations = result.Operations
        }, JsonOptions);
    }

    private static string FormatError(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions);
}
