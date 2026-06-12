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
public sealed partial class OpenApiDocumentReader : IOpenApiDocumentReader
{
    /// <summary>
    /// Matches the document's top-level version declaration for the
    /// OpenAPI 3.1 compatibility downgrade. Replaced once, on the first match.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex("""("openapi"\s*:\s*")3\.1(?:\.\d+)?(")""")]
    private static partial System.Text.RegularExpressions.Regex OpenApi31VersionRegex();

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

        var warnings = new List<string>();

        // OpenAPI 3.1 compatibility mode: the 1.6.x reader rejects 3.1
        // documents outright ("specification version '3.1.x' is not
        // supported") — yet 3.1 is what .NET 10's built-in generator emits by
        // default. 3.1 is a superset refinement of 3.0 for the constructs this
        // converter consumes (paths, operations, parameters, $refs), so we
        // parse it as 3.0 and surface a warning. JSON-Schema-only keywords
        // produce non-fatal diagnostics and are ignored.
        var effectiveText = openApiText;
        var versionMatch = OpenApi31VersionRegex().Match(openApiText);
        if (versionMatch.Success)
        {
            effectiveText = OpenApi31VersionRegex().Replace(openApiText, "${1}3.0.3${2}", 1);
            warnings.Add("OpenAPI 3.1 document read in 3.0 compatibility mode — " +
                         "JSON-Schema-only keywords (type arrays, $defs, …) are ignored.");
        }

        try
        {
            var reader = new OpenApiStringReader();
            var document = reader.Read(effectiveText, out var diagnostic);

            return new OpenApiReadResult
            {
                Document = document,
                Errors = diagnostic?.Errors.Select(e => e.Message).ToList() ?? [],
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new OpenApiReadResult
            {
                Errors = [ex.Message],
                Warnings = warnings
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

    /// <summary>Non-fatal notes, e.g. the OpenAPI 3.1 compatibility-mode downgrade.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>True when a document with a usable paths collection was produced.</summary>
    public bool HasUsablePaths => Document?.Paths is { };

    /// <summary>True when no diagnostics or exceptions occurred at all.</summary>
    public bool IsClean => Document is not null && Errors.Count == 0;
}
