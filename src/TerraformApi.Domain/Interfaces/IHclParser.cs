using TerraformApi.Domain.Models.Hcl;

namespace TerraformApi.Domain.Interfaces;

/// <summary>
/// Parses HCL source text into an AST.
/// Supports the subset used by APIM Terraform configs: nested objects,
/// arrays of objects, string/number/bool/null literals, interpolated strings,
/// heredocs, and comments.
/// </summary>
public interface IHclParser
{
    /// <summary>
    /// Parses HCL source into an AST.
    /// Throws <see cref="HclParseException"/> with line/column on syntax errors.
    /// </summary>
    HclDocument Parse(string source);

    /// <summary>
    /// Best-effort parsing: never throws; collects diagnostics instead.
    /// </summary>
    HclParseResult TryParse(string source);
}

/// <summary>Result of a tolerant parse attempt.</summary>
public sealed record HclParseResult
{
    public HclDocument? Document { get; init; }
    public List<HclParseDiagnostic> Diagnostics { get; init; } = [];
    public bool IsSuccess => Document is not null
        && !Diagnostics.Any(d => d.Severity == HclDiagnosticSeverity.Error);
}

/// <summary>A single parse diagnostic with source position.</summary>
public sealed record HclParseDiagnostic
{
    public required string Message { get; init; }
    public required HclDiagnosticSeverity Severity { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
}

/// <summary>Diagnostic severity levels.</summary>
public enum HclDiagnosticSeverity
{
    Warning,
    Error
}

/// <summary>Thrown by <see cref="IHclParser.Parse"/> on invalid HCL syntax.</summary>
public sealed class HclParseException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public HclParseException(string message, int line, int column)
        : base($"{message} (line {line}, column {column})")
    {
        Line = line;
        Column = column;
    }
}
