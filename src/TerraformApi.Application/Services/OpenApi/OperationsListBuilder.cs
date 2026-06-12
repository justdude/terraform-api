using Microsoft.OpenApi.Models;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Static helper that maps a parsed <see cref="OpenApiDocument"/> onto the
/// unified <see cref="OperationsListResult"/> shared with the Terraform parser.
/// Pure mapping — document reading lives in <see cref="OpenApiDocumentReader"/>.
/// </summary>
internal static class OperationsListBuilder
{
    public static OperationsListResult Build(OpenApiDocument document, string sourceUrl)
    {
        var apiInfo = new OperationsApiInfo
        {
            Title = document.Info?.Title ?? "Unknown",
            Version = document.Info?.Version ?? "Unknown",
            Description = string.IsNullOrWhiteSpace(document.Info?.Description) ? null : document.Info!.Description,
            SourceUrl = sourceUrl,
            Source = "openapi"
        };

        var operations = new List<OperationInfo>();

        foreach (var pathItem in document.Paths)
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
