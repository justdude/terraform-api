using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

/// <summary>
/// Orchestrates the full conversion pipeline: settings validation → OpenAPI parsing →
/// Terraform generation (or merging with an existing config) → APIM naming validation.
/// All pipeline steps are coordinated here so that individual services remain focused
/// on their single responsibility.
/// </summary>
public sealed class ConversionOrchestratorService : IConversionOrchestrator
{
    private readonly IOpenApiParser _parser;
    private readonly ITerraformGenerator _generator;
    private readonly ITerraformMerger _merger;
    private readonly IApimNamingValidator _namingValidator;

    public ConversionOrchestratorService(
        IOpenApiParser parser,
        ITerraformGenerator generator,
        ITerraformMerger merger,
        IApimNamingValidator namingValidator)
    {
        _parser = parser;
        _generator = generator;
        _merger = merger;
        _namingValidator = namingValidator;
    }

    /// <summary>
    /// Validates settings, parses the OpenAPI JSON, generates Terraform HCL, and
    /// returns a <see cref="ConversionResult"/> containing the output or any errors.
    /// Non-fatal naming issues are surfaced as warnings rather than errors.
    /// </summary>
    public ConversionResult Convert(string openApiJson, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(openApiJson);
        ArgumentNullException.ThrowIfNull(settings);

        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            var validationErrors = ValidateSettings(settings);
            if (validationErrors.Count > 0)
            {
                return new ConversionResult
                {
                    Success = false,
                    Errors = validationErrors
                };
            }

            var configuration = _parser.Parse(openApiJson, settings);

            warnings.AddRange(ValidateGeneratedNames(configuration));

            var terraform = _generator.Generate(configuration);

            return new ConversionResult
            {
                Success = true,
                TerraformConfig = terraform,
                Configuration = configuration,
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            errors.Add(ex is InvalidOperationException ? ex.Message : $"Conversion failed: {ex.Message}");
            return new ConversionResult
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Like <see cref="Convert"/> but merges the newly-generated Terraform with
    /// <paramref name="existingTerraform"/>, preserving any custom operation blocks
    /// not present in the updated OpenAPI spec.
    /// </summary>
    public ConversionResult Update(string openApiJson, string existingTerraform, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(openApiJson);
        ArgumentNullException.ThrowIfNull(existingTerraform);
        ArgumentNullException.ThrowIfNull(settings);

        var warnings = new List<string>();
        var errors = new List<string>();

        try
        {
            var validationErrors = ValidateSettings(settings);
            if (validationErrors.Count > 0)
            {
                return new ConversionResult
                {
                    Success = false,
                    Errors = validationErrors
                };
            }

            var configuration = _parser.Parse(openApiJson, settings);

            warnings.AddRange(ValidateGeneratedNames(configuration));

            var terraform = _merger.Merge(existingTerraform, configuration);

            return new ConversionResult
            {
                Success = true,
                TerraformConfig = terraform,
                Configuration = configuration,
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            errors.Add(ex is InvalidOperationException ? ex.Message : $"Update failed: {ex.Message}");
            return new ConversionResult
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// Validates the conversion settings before parsing begins. Currently checks the
    /// resource group name and the optional explicit API name.
    /// Returns an empty list when all settings are valid.
    /// </summary>
    private List<string> ValidateSettings(ConversionSettings settings)
    {
        var errors = new List<string>();

        var rgResult = _namingValidator.ValidateResourceGroupName(settings.StageGroupName);
        if (!rgResult.IsValid)
            errors.AddRange(rgResult.Errors.Select(e => $"StageGroupName: {e}"));

        if (!string.IsNullOrEmpty(settings.ApiName))
        {
            var apiResult = _namingValidator.ValidateApiName(settings.ApiName);
            if (!apiResult.IsValid)
                errors.AddRange(apiResult.Errors.Select(e => $"ApiName: {e}"));
        }

        return errors;
    }

    /// <summary>
    /// Validates the names that were auto-generated during parsing (API name, path,
    /// and all operation IDs + display names) against Microsoft's APIM naming rules.
    /// Violations become warnings rather than fatal errors because the generator
    /// sanitises names before output.
    /// </summary>
    private List<string> ValidateGeneratedNames(ApimConfiguration configuration)
    {
        var warnings = new List<string>();

        var apiNameResult = _namingValidator.ValidateApiName(configuration.Api.Name);
        if (!apiNameResult.IsValid)
            warnings.AddRange(apiNameResult.Errors.Select(e => $"Generated API name warning: {e}"));

        var pathResult = _namingValidator.ValidateApiPath(configuration.Api.Path);
        if (!pathResult.IsValid)
            warnings.AddRange(pathResult.Errors.Select(e => $"Generated API path warning: {e}"));

        foreach (var op in configuration.ApiOperations)
        {
            var opResult = _namingValidator.ValidateOperationId(op.OperationId);
            if (!opResult.IsValid)
                warnings.AddRange(opResult.Errors.Select(e => $"Operation '{op.OperationId}': {e}"));

            var displayResult = _namingValidator.ValidateDisplayName(op.DisplayName);
            if (!displayResult.IsValid)
                warnings.AddRange(displayResult.Errors.Select(e => $"Operation display name '{op.DisplayName}': {e}"));
        }

        return warnings;
    }
}
