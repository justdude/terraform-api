using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using ModelContextProtocol.Server;

namespace TerraformApi.Mcp.Tools;

/// <summary>
/// MCP tool that fetches an OpenAPI specification from a URL and returns a structured
/// list of all operations with their methods, paths, parameters, and metadata.
/// No APIM-specific settings required — this is a pure discovery/inspection tool.
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
        [Description("URL to the OpenAPI/Swagger JSON endpoint (e.g. 'https://api.example.com/swagger/v1/swagger.json')")] string openApiUrl,
        CancellationToken cancellationToken = default)
    {
        return await FetchOperationsCore(httpClient, openApiUrl, cancellationToken);
    }

    /// <summary>
    /// Core implementation separated for testability.
    /// </summary>
    internal static async Task<string> FetchOperationsCore(HttpClient httpClient, string openApiUrl, CancellationToken cancellationToken = default)
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

        return ParseAndFormat(json, openApiUrl);
    }

    /// <summary>
    /// Parses an OpenAPI JSON string and formats the operations list.
    /// Separated so it can be tested without HTTP.
    /// </summary>
    internal static string ParseAndFormat(string openApiJson, string sourceUrl = "inline")
    {
        OpenApiDocument doc;
        OpenApiDiagnostic diagnostic;

        try
        {
            var reader = new OpenApiStringReader();
            doc = reader.Read(openApiJson, out diagnostic);
        }
        catch (Exception ex)
        {
            return FormatError($"Failed to parse OpenAPI document: {ex.Message}");
        }

        if (doc?.Paths == null || doc.Paths.Count == 0)
        {
            var errors = diagnostic?.Errors.Select(e => e.Message).ToList() ?? [];
            if (errors.Count > 0)
                return FormatError($"OpenAPI parse errors: {string.Join("; ", errors)}");

            return FormatError("No API paths found in the OpenAPI document.");
        }

        var apiInfo = new ApiInfoDto
        {
            Title = doc.Info?.Title ?? "Unknown",
            Version = doc.Info?.Version ?? "Unknown",
            Description = string.IsNullOrWhiteSpace(doc.Info?.Description) ? null : doc.Info!.Description,
            SourceUrl = sourceUrl
        };

        var operations = new List<OperationDto>();

        foreach (var pathItem in doc.Paths)
        {
            foreach (var operation in pathItem.Value.Operations)
            {
                var parameters = BuildParameters(operation.Value, pathItem.Value);

                operations.Add(new OperationDto
                {
                    Method = operation.Key.ToString().ToUpperInvariant(),
                    UrlTemplate = pathItem.Key.TrimStart('/'),
                    Path = pathItem.Key,
                    OperationId = operation.Value.OperationId,
                    Description = string.IsNullOrWhiteSpace(operation.Value.Summary)
                        ? (string.IsNullOrWhiteSpace(operation.Value.Description) ? null : operation.Value.Description)
                        : operation.Value.Summary,
                    Tags = operation.Value.Tags?.Select(t => t.Name).ToList(),
                    Parameters = parameters.Count > 0 ? parameters : null,
                    RequestBodyContentTypes = GetRequestBodyTypes(operation.Value),
                    ResponseCodes = GetResponseCodes(operation.Value)
                });
            }
        }

        var result = new FetchResultDto
        {
            Api = apiInfo,
            TotalOperations = operations.Count,
            Operations = operations
        };

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    private static List<ParameterDto> BuildParameters(OpenApiOperation operation, OpenApiPathItem pathItem)
    {
        var parameters = new List<ParameterDto>();

        // Merge path-level + operation-level params (operation-level wins on conflict)
        var allParams = (pathItem.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .GroupBy(p => (p.Name, p.In))
            .Select(g => g.Last())
            .ToList();

        foreach (var param in allParams)
        {
            parameters.Add(new ParameterDto
            {
                Name = param.Name,
                In = param.In?.ToString()?.ToLowerInvariant() ?? "unknown",
                Type = MapSchemaType(param.Schema),
                Required = param.Required,
                Description = string.IsNullOrWhiteSpace(param.Description) ? null : param.Description
            });
        }

        return parameters;
    }

    private static List<string>? GetRequestBodyTypes(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content == null || operation.RequestBody.Content.Count == 0)
            return null;

        return operation.RequestBody.Content.Keys.ToList();
    }

    private static List<int>? GetResponseCodes(OpenApiOperation operation)
    {
        if (operation.Responses == null || operation.Responses.Count == 0)
            return null;

        var codes = new List<int>();
        foreach (var r in operation.Responses)
        {
            if (int.TryParse(r.Key, out var code))
                codes.Add(code);
        }

        return codes.Count > 0 ? codes : null;
    }

    private static string MapSchemaType(OpenApiSchema? schema)
    {
        if (schema == null) return "string";
        return schema.Type switch
        {
            "integer" => schema.Format == "int64" ? "int64" : "integer",
            "number" => schema.Format == "double" ? "double" : "number",
            "boolean" => "boolean",
            "array" => $"array<{MapSchemaType(schema.Items)}>",
            "object" => "object",
            _ => "string"
        };
    }

    private static string FormatError(string message) =>
        JsonSerializer.Serialize(new { error = message }, JsonOptions);

    // ── DTOs for JSON serialization ──────────────────────────────────

    private sealed class FetchResultDto
    {
        public required ApiInfoDto Api { get; init; }
        public int TotalOperations { get; init; }
        public required List<OperationDto> Operations { get; init; }
    }

    private sealed class ApiInfoDto
    {
        public required string Title { get; init; }
        public required string Version { get; init; }
        public string? Description { get; init; }
        public string? SourceUrl { get; init; }
    }

    private sealed class OperationDto
    {
        public required string Method { get; init; }
        public required string UrlTemplate { get; init; }
        public required string Path { get; init; }
        public string? OperationId { get; init; }
        public string? Description { get; init; }
        public List<string>? Tags { get; init; }
        public List<ParameterDto>? Parameters { get; init; }
        public List<string>? RequestBodyContentTypes { get; init; }
        public List<int>? ResponseCodes { get; init; }
    }

    private sealed class ParameterDto
    {
        public required string Name { get; init; }
        public required string In { get; init; }
        public required string Type { get; init; }
        public bool Required { get; init; }
        public string? Description { get; init; }
    }
}
