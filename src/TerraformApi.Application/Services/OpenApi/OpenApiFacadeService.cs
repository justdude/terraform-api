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

        // Diagnostics are tolerated ONLY when the document was down-leveled from
        // OpenAPI 3.1: post-downgrade, legitimate 3.1-only keywords
        // (type arrays, $defs, …) surface as non-fatal diagnostics. For genuine
        // 3.0 documents any reader error is a real spec violation (e.g. a
        // missing required 'name' on a parameter) and stays fatal — otherwise
        // the converter would emit invalid Terraform. Strict all-diagnostic
        // reporting lives in the validate endpoint/tool.
        var unusable = read.Document is null || (read.Errors.Count > 0 && !read.DowngradedFrom31);
        if (unusable)
        {
            var reason = read.Errors.Count > 0 ? string.Join("; ", read.Errors) : "Unknown error";
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {reason}");
        }

        // Missing settings become {tag} placeholders (idempotent when the
        // orchestrator already normalized upstream).
        (settings, _) = ApimPlaceholders.Normalize(settings);

        // read.Document is non-null here (the "unusable" guard covers null).
        var configuration = ApimConfigurationBuilder.Build(read.Document!, settings, _namingValidator);

        // Surface reader warnings (3.1 compat mode) to the caller.
        return read.Warnings.Count > 0
            ? configuration with { Warnings = [.. configuration.Warnings, .. read.Warnings] }
            : configuration;
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

        return OperationsListBuilder.Build(read.Document, sourceUrl, read.Warnings);
    }
}
