using Microsoft.Extensions.DependencyInjection;
using TerraformApi.Application.Services;
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

        return services;
    }
}
