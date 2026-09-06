using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Application.Services.Hcl;

/// <summary>
/// Helpers for keeping a re-rendered HCL document on the same line endings as its
/// source. The writer defaults to <c>"\n"</c>; once any node is dirty it takes the
/// canonical render path and every writer-emitted newline would be LF, mixing with
/// the CRLFs still inside the verbatim slices of a Windows-authored file. Every
/// caller that may re-render an edited document should write with these options.
/// </summary>
public static class HclLineEndings
{
    /// <summary>CRLF when the source contains any CRLF, otherwise LF.</summary>
    public static string Detect(string? source) =>
        source?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";

    /// <summary>Write options whose line ending matches <paramref name="source"/>.</summary>
    public static HclWriteOptions OptionsFor(string? source) =>
        new() { LineEnding = Detect(source) };
}
