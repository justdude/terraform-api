using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Facade over all OpenAPI functionality — the single service instance behind
/// both <see cref="IOpenApiParser"/> (OpenAPI → <see cref="ApimConfiguration"/>
/// for Terraform generation) and <see cref="IOpenApiOperationsFetcher"/>
/// (OpenAPI → unified <see cref="OperationsListResult"/>).
///
/// The facade holds no logic of its own: document reading is centralized in
/// <see cref="OpenApiDocumentReader"/> (the only Microsoft.OpenApi.Readers
/// call site) and mapping lives in the static helpers
/// <see cref="ApimConfigurationBuilder"/> and <see cref="OperationsListBuilder"/>.
/// Registered once in DI; both interfaces resolve to the same instance.
/// </summary>
public sealed class OpenApiFacadeService : IOpenApiParser, IOpenApiOperationsFetcher
{
    private readonly IApimNamingValidator _namingValidator;
    private readonly IOpenApiDocumentReader _documentReader;

    public OpenApiFacadeService(IApimNamingValidator namingValidator, IOpenApiDocumentReader documentReader)
    {
        _namingValidator = namingValidator;
        _documentReader = documentReader;
    }

    /// <summary>Convenience constructor for plain library use — wires the default reader.</summary>
    public OpenApiFacadeService(IApimNamingValidator namingValidator)
        : this(namingValidator, new OpenApiDocumentReader())
    {
    }

    /// <summary>
    /// Parses OpenAPI JSON into the APIM configuration model.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the document cannot be read or contains fatal OpenAPI errors —
    /// the contract expected by <see cref="IConversionOrchestrator"/>.
    /// </exception>
    public ApimConfiguration Parse(string openApiJson, ConversionSettings settings)
    {
        var read = _documentReader.Read(openApiJson);

        // Tolerant by design: diagnostics with a USABLE document (3.1 compat
        // mode, vendor extensions, JSON-Schema-only keywords) do not block
        // conversion. Diagnostics are fatal only when the document yielded no
        // paths either — the 1.6 reader is YAML-lenient and coerces garbage
        // input into an empty document instead of throwing. Strict checking
        // lives in the validate endpoint/tool, which surfaces all diagnostics.
        var unusable = read.Document is null
            || (read.Errors.Count > 0 && (read.Document.Paths is null || read.Document.Paths.Count == 0));
        if (unusable)
        {
            var reason = read.Errors.Count > 0 ? string.Join("; ", read.Errors) : "Unknown error";
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {reason}");
        }

        // Missing settings become {tag} placeholders (idempotent when the
        // orchestrator already normalized upstream).
        (settings, _) = ApimPlaceholders.Normalize(settings);

        return ApimConfigurationBuilder.Build(read.Document, settings, _namingValidator);
    }

    /// <summary>
    /// Parses OpenAPI JSON into the unified operations list. Never throws —
    /// failures are reported via <see cref="OperationsListResult.Success"/>,
    /// the contract expected by the fetch-operations endpoint and MCP tool.
    /// </summary>
    public OperationsListResult ParseOperations(string openApiJson, string sourceUrl = "inline")
    {
        var read = _documentReader.Read(openApiJson);

        if (read.Document?.Paths is null || read.Document.Paths.Count == 0)
        {
            var message = read.Errors.Count > 0
                ? $"OpenAPI parse errors: {string.Join("; ", read.Errors)}"
                : "No API paths found in the OpenAPI document.";

            return new OperationsListResult { Success = false, Error = message };
        }

        return OperationsListBuilder.Build(read.Document, sourceUrl);
    }
}
