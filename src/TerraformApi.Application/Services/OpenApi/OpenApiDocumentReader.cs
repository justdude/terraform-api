using System.Text.Json;
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
    /// <summary>Rewrites the JSON <c>"openapi": "3.1.x"</c> version value to 3.0.3 (first match only).</summary>
    [System.Text.RegularExpressions.GeneratedRegex("""("openapi"\s*:\s*")3\.1(?:\.\d+)?(")""")]
    private static partial System.Text.RegularExpressions.Regex JsonVersionRegex();

    /// <summary>Matches a top-level (column 0) YAML <c>openapi: 3.1.x</c> declaration.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("""(?m)^(openapi\s*:\s*["']?)3\.1(?:\.\d+)?""")]
    private static partial System.Text.RegularExpressions.Regex YamlVersionRegex();

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
        var (effectiveText, downgraded) = ApplyOpenApi31Downgrade(openApiText);
        if (downgraded)
        {
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
                Warnings = warnings,
                DowngradedFrom31 = downgraded
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new OpenApiReadResult
            {
                Errors = [ex.Message],
                Warnings = warnings,
                DowngradedFrom31 = downgraded
            };
        }
    }

    /// <summary>
    /// Detects a genuine <b>document-level</b> 3.1 version and, if present,
    /// rewrites it to 3.0.3 for the 1.6 reader. Detection is anchored to the
    /// root so a 3.1 string buried in an example/default of a real 3.0 document
    /// never triggers a downgrade (JSON) and a top-level YAML declaration is
    /// also recognized (the byte-identical JSON form was already supported).
    /// </summary>
    private static (string Text, bool Downgraded) ApplyOpenApi31Downgrade(string text)
    {
        // JSON: read the ROOT openapi property precisely; only downgrade when it
        // is genuinely 3.1. A 3.1 string inside an example never matches.
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("openapi", out var version) &&
                version.ValueKind == JsonValueKind.String &&
                version.GetString() is { } v &&
                v.StartsWith("3.1", StringComparison.Ordinal))
            {
                return (JsonVersionRegex().Replace(text, "${1}3.0.3${2}", 1), true);
            }

            // Parsed as JSON and the root is not 3.1 → never downgrade.
            return (text, false);
        }
        catch (JsonException)
        {
            // Not JSON (YAML or malformed) — fall through to the YAML check.
        }

        if (YamlVersionRegex().IsMatch(text))
            return (YamlVersionRegex().Replace(text, "${1}3.0.3", 1), true);

        return (text, false);
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

    /// <summary>
    /// True when the document declared OpenAPI 3.1 and was read in 3.0
    /// compatibility mode. Used to decide whether reader diagnostics are
    /// tolerable (3.1-only keywords) or fatal (genuine 3.0 spec violations).
    /// </summary>
    public bool DowngradedFrom31 { get; init; }

    /// <summary>True when a document with a usable paths collection was produced.</summary>
    public bool HasUsablePaths => Document?.Paths is { };

    /// <summary>True when no diagnostics or exceptions occurred at all.</summary>
    public bool IsClean => Document is not null && Errors.Count == 0;
}
