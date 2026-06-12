using Microsoft.Extensions.DependencyInjection;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IApimNamingValidator, ApimNamingValidatorService>();
        services.AddSingleton<IOpenApiParser, OpenApiParserService>();
        services.AddSingleton<ITerraformGenerator, TerraformGeneratorService>();
        services.AddSingleton<ITerraformMerger, TerraformMergerService>();
        services.AddSingleton<IConversionOrchestrator, ConversionOrchestratorService>();
        services.AddSingleton<IEnvironmentTransformer, EnvironmentTransformerService>();
        services.AddSingleton<ITerraformOperationsParser, TerraformOperationsParserService>();
        services.AddSingleton<IOpenApiOperationsFetcher, OpenApiOperationsFetcherService>();

        // HCL AST pipeline (sync engine)
        services.AddSingleton<IHclParser, HclParserService>();
        services.AddSingleton<IHclWriter, HclWriterService>();
        services.AddSingleton<IApimTerraformReader, Services.Apim.ApimTerraformReaderService>();
        services.AddSingleton<IApimTerraformWriter, Services.Apim.ApimTerraformWriterService>();
        services.AddSingleton<IOperationCommentBuilder, Services.Sync.OperationCommentBuilderService>();
        services.AddSingleton<Services.Sync.TerraformInterpolationResolver>();
        services.AddSingleton<IApimTemplateProfileDetector, Services.Sync.ApimTemplateProfileDetectorService>();
        services.AddSingleton<IOperationMatcher, Services.Sync.OperationMatcherService>();

        return services;
    }
}
