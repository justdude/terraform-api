using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Parses an OpenAPI JSON specification and returns a structured operations list
/// using the unified <see cref="OperationsListResult"/> format shared with the Terraform parser.
/// </summary>
public sealed class OpenApiOperationsFetcherService : IOpenApiOperationsFetcher
{
    /// <inheritdoc />
    public OperationsListResult ParseOperations(string openApiJson, string sourceUrl = "inline")
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
            return new OperationsListResult
            {
                Success = false,
                Error = $"Failed to parse OpenAPI document: {ex.Message}"
            };
        }

        if (doc?.Paths == null || doc.Paths.Count == 0)
        {
            var errors = diagnostic?.Errors.Select(e => e.Message).ToList() ?? [];
            var msg = errors.Count > 0
                ? $"OpenAPI parse errors: {string.Join("; ", errors)}"
                : "No API paths found in the OpenAPI document.";

            return new OperationsListResult { Success = false, Error = msg };
        }

        var apiInfo = new OperationsApiInfo
        {
            Title = doc.Info?.Title ?? "Unknown",
            Version = doc.Info?.Version ?? "Unknown",
            Description = string.IsNullOrWhiteSpace(doc.Info?.Description) ? null : doc.Info!.Description,
            SourceUrl = sourceUrl,
            Source = "openapi"
        };

        var operations = new List<OperationInfo>();

        foreach (var pathItem in doc.Paths)
        {
            foreach (var operation in pathItem.Value.Operations)
            {
                var parameters = BuildParameters(operation.Value, pathItem.Value);

                operations.Add(new OperationInfo
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

        return new OperationsListResult
        {
            Success = true,
            Api = apiInfo,
            TotalOperations = operations.Count,
            Operations = operations
        };
    }

    private static List<ParameterInfo> BuildParameters(OpenApiOperation operation, OpenApiPathItem pathItem)
    {
        var parameters = new List<ParameterInfo>();

        var allParams = (pathItem.Parameters ?? [])
            .Concat(operation.Parameters ?? [])
            .GroupBy(p => (p.Name, p.In))
            .Select(g => g.Last())
            .ToList();

        foreach (var param in allParams)
        {
            parameters.Add(new ParameterInfo
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
}
