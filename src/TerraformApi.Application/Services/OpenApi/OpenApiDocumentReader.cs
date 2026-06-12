using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// The single place in the codebase that touches <c>Microsoft.OpenApi.Readers</c>.
/// Every consumer (facade service, MCP tools, API controllers) reads OpenAPI
/// documents through this helper, so a future migration to the
/// Microsoft.OpenApi 3.x reader API (<c>OpenApiDocument.Parse</c>, which
/// replaced <c>OpenApiStringReader</c>) is a one-file change.
/// Pinned to the 1.6.x line because Swashbuckle 7.x in the API host depends
/// on Microsoft.OpenApi 1.6.x.
/// </summary>
public static class OpenApiDocumentReader
{
    /// <summary>
    /// Reads an OpenAPI JSON/YAML string. Never throws — reader exceptions and
    /// diagnostics are collected into <see cref="OpenApiReadResult.Errors"/>.
    /// </summary>
    public static OpenApiReadResult Read(string openApiText)
    {
        if (string.IsNullOrWhiteSpace(openApiText))
        {
            return new OpenApiReadResult
            {
                Errors = ["OpenAPI content is empty."]
            };
        }

        try
        {
            var reader = new OpenApiStringReader();
            var document = reader.Read(openApiText, out var diagnostic);

            return new OpenApiReadResult
            {
                Document = document,
                Errors = diagnostic?.Errors.Select(e => e.Message).ToList() ?? []
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new OpenApiReadResult
            {
                Errors = [ex.Message]
            };
        }
    }
}

/// <summary>Outcome of reading an OpenAPI document.</summary>
public sealed record OpenApiReadResult
{
    /// <summary>The parsed document; may be non-null even when diagnostics exist.</summary>
    public OpenApiDocument? Document { get; init; }

    /// <summary>Reader diagnostics and/or the thrown exception message.</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>True when a document with a usable paths collection was produced.</summary>
    public bool HasUsablePaths => Document?.Paths is { };

    /// <summary>True when no diagnostics or exceptions occurred at all.</summary>
    public bool IsClean => Document is not null && Errors.Count == 0;
}
