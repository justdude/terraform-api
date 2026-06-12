using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;

namespace TerraformApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Self-sufficient outside ASP.NET/Generic Host containers: when the host
        // has not registered logging, fall back to NullLogger (TryAdd — a real
        // logging registration always wins).
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSingleton<IApimNamingValidator, ApimNamingValidatorService>();

        // OpenAPI facade (ACC2): ONE instance serves both interfaces. The facade
        // delegates to static helpers; document reading is centralized in
        // OpenApiDocumentReader (the only Microsoft.OpenApi.Readers call site).
        services.AddSingleton<Services.OpenApi.OpenApiFacadeService>();
        services.AddSingleton<IOpenApiParser>(sp => sp.GetRequiredService<Services.OpenApi.OpenApiFacadeService>());
        services.AddSingleton<IOpenApiOperationsFetcher>(sp => sp.GetRequiredService<Services.OpenApi.OpenApiFacadeService>());

        services.AddSingleton<ITerraformGenerator, TerraformGeneratorService>();
        services.AddSingleton<ITerraformMerger, TerraformMergerService>();
        services.AddSingleton<IConversionOrchestrator, ConversionOrchestratorService>();
        services.AddSingleton<IEnvironmentTransformer, EnvironmentTransformerService>();
        services.AddSingleton<ITerraformOperationsParser, TerraformOperationsParserService>();
        services.AddSingleton<IApimProductGenerator, ApimProductGeneratorService>();

        // HCL AST pipeline (sync engine)
        services.AddSingleton<IHclParser, HclParserService>();
        services.AddSingleton<IHclWriter, HclWriterService>();
        services.AddSingleton<IApimTerraformReader, Services.Apim.ApimTerraformReaderService>();
        services.AddSingleton<IApimTerraformWriter, Services.Apim.ApimTerraformWriterService>();
        services.AddSingleton<IOperationCommentBuilder, Services.Sync.OperationCommentBuilderService>();
        services.AddSingleton<Services.Sync.TerraformInterpolationResolver>();
        services.AddSingleton<IApimTemplateProfileDetector, Services.Sync.ApimTemplateProfileDetectorService>();
        services.AddSingleton<IOperationMatcher, Services.Sync.OperationMatcherService>();
        services.AddSingleton<IDuplicateDetector, Services.Sync.DuplicateDetectorService>();
        services.AddSingleton<IAppendOnlySynchronizer, Services.Sync.AppendOnlySynchronizerService>();
        services.AddSingleton<IApimTemplateProfileApplier, Services.Sync.ApimTemplateProfileApplierService>();
        services.AddSingleton<IOperationExecutionGraphBuilder, Services.Sync.OperationExecutionGraphBuilderService>();
        services.AddSingleton<ISyncOrchestrator, SyncOrchestratorService>();

        // Package-level facade: one injectable entry point over the whole engine.
        services.AddSingleton<TerraformApiFacade>();

        return services;
    }
}
