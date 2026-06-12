using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Reads OpenAPI text into a parsed document. This abstraction exists so the
/// underlying reader implementation is swappable (tests can substitute it, and
/// the future migration to the Microsoft.OpenApi 3.x reader API —
/// <c>OpenApiDocument.Parse</c>, which replaced <c>OpenApiStringReader</c> —
/// only needs a new implementation of this interface).
///
/// Lives in the Application layer (not Domain) deliberately: the result type
/// exposes the vendor <see cref="OpenApiDocument"/> model, which must not leak
/// into the dependency-free Domain project.
/// </summary>
public interface IOpenApiDocumentReader
{
    /// <summary>
    /// Reads an OpenAPI JSON/YAML string. Never throws — reader exceptions and
    /// diagnostics are collected into <see cref="OpenApiReadResult.Errors"/>.
    /// </summary>
    OpenApiReadResult Read(string openApiText);
}

/// <summary>
/// Default reader built on <c>Microsoft.OpenApi.Readers</c> — the single place
/// in the codebase that touches <see cref="OpenApiStringReader"/>. Stateless;
/// registered as a singleton. Pinned to the 1.6.x line because Swashbuckle 7.x
/// in the API host depends on Microsoft.OpenApi 1.6.x.
/// </summary>
public sealed class OpenApiDocumentReader : IOpenApiDocumentReader
{
    /// <inheritdoc />
    public OpenApiReadResult Read(string openApiText)
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
