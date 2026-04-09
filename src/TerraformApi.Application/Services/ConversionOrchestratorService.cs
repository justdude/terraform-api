using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

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

    public ConversionResult Convert(string openApiJson, ConversionSettings settings)
    {
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
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
            return new ConversionResult
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

    public ConversionResult Update(string openApiJson, string existingTerraform, ConversionSettings settings)
    {
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
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
            return new ConversionResult
            {
                Success = false,
                Errors = errors,
                Warnings = warnings
            };
        }
    }

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
