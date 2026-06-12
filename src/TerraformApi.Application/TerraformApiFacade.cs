using Microsoft.Extensions.Logging.Abstractions;
using TerraformApi.Application.Services;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Application.Services.OpenApi;
using TerraformApi.Application.Services.Sync;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application;

/// <summary>
/// Single entry point for the TerraformApi.Application NuGet package — covers
/// the whole engine without requiring an ASP.NET host, an MCP client, or a DI
/// container:
///
/// <code>
/// var facade = TerraformApiFacade.Create();
/// var result = facade.ConvertOpenApiToTerraform(openApiJson, new ConversionSettings { Environment = "dev" });
/// </code>
///
/// All APIM settings are optional — missing values are generated as
/// replaceable placeholder tags (see <see cref="ApimPlaceholders"/>).
/// DI consumers should instead call
/// <see cref="DependencyInjection.AddApplicationServices"/> and inject the
/// individual interfaces; this facade exists for plain library usage.
/// </summary>
public sealed class TerraformApiFacade
{
    private readonly IConversionOrchestrator _conversionOrchestrator;
    private readonly ISyncOrchestrator _syncOrchestrator;
    private readonly IEnvironmentTransformer _environmentTransformer;
    private readonly IApimProductGenerator _productGenerator;
    private readonly IOpenApiOperationsFetcher _openApiOperationsFetcher;
    private readonly ITerraformOperationsParser _terraformOperationsParser;

    public TerraformApiFacade(
        IConversionOrchestrator conversionOrchestrator,
        ISyncOrchestrator syncOrchestrator,
        IEnvironmentTransformer environmentTransformer,
        IApimProductGenerator productGenerator,
        IOpenApiOperationsFetcher openApiOperationsFetcher,
        ITerraformOperationsParser terraformOperationsParser)
    {
        _conversionOrchestrator = conversionOrchestrator;
        _syncOrchestrator = syncOrchestrator;
        _environmentTransformer = environmentTransformer;
        _productGenerator = productGenerator;
        _openApiOperationsFetcher = openApiOperationsFetcher;
        _terraformOperationsParser = terraformOperationsParser;
    }

    /// <summary>Wires the full engine without a DI container.</summary>
    public static TerraformApiFacade Create()
    {
        var namingValidator = new ApimNamingValidatorService();
        var openApiFacade = new OpenApiFacadeService(namingValidator);
        var generator = new TerraformGeneratorService();
        var merger = new TerraformMergerService(generator);
        var conversionOrchestrator = new ConversionOrchestratorService(
            openApiFacade, generator, merger, namingValidator);

        var hclParser = new HclParserService();
        var hclWriter = new HclWriterService();
        var apimReader = new ApimTerraformReaderService(hclParser);
        var commentBuilder = new OperationCommentBuilderService();
        var apimWriter = new ApimTerraformWriterService(hclWriter, apimReader, commentBuilder);
        var resolver = new TerraformInterpolationResolver();
        var profileDetector = new ApimTemplateProfileDetectorService();
        var duplicateDetector = new DuplicateDetectorService();
        var synchronizer = new AppendOnlySynchronizerService(
            new OperationMatcherService(resolver),
            duplicateDetector,
            profileDetector,
            commentBuilder,
            hclWriter,
            apimWriter,
            resolver,
            NullLogger<AppendOnlySynchronizerService>.Instance);

        var syncOrchestrator = new SyncOrchestratorService(
            openApiFacade,
            apimReader,
            apimWriter,
            hclParser,
            synchronizer,
            profileDetector,
            duplicateDetector,
            new ApimTemplateProfileApplierService(resolver),
            new OperationExecutionGraphBuilderService());

        return new TerraformApiFacade(
            conversionOrchestrator,
            syncOrchestrator,
            new EnvironmentTransformerService(),
            new ApimProductGeneratorService(generator, namingValidator),
            openApiFacade,
            new TerraformOperationsParserService());
    }

    /// <summary>Converts an OpenAPI JSON specification to a fresh APIM Terraform configuration.</summary>
    public ConversionResult ConvertOpenApiToTerraform(string openApiJson, ConversionSettings? settings = null) =>
        _conversionOrchestrator.Convert(openApiJson, settings ?? new ConversionSettings());

    /// <summary>Merges a new OpenAPI spec into existing Terraform, preserving custom operations.</summary>
    public ConversionResult UpdateTerraform(string openApiJson, string existingTerraform, ConversionSettings? settings = null) =>
        _conversionOrchestrator.Update(openApiJson, existingTerraform, settings ?? new ConversionSettings());

    /// <summary>
    /// Append-only sync of existing Terraform with an OpenAPI spec — never
    /// deletes anything; returns the final HCL, a diff report and an execution graph.
    /// </summary>
    public SyncResult Sync(SyncRequest request) =>
        _syncOrchestrator.Sync(request);

    /// <summary>Read-only analysis of an existing Terraform config (groups, style, duplicates).</summary>
    public AnalyzeResult AnalyzeTerraform(string existingTerraform) =>
        _syncOrchestrator.Analyze(existingTerraform);

    /// <summary>Templatize (literals → ${...}) or Resolve (${...} → literals) an existing config.</summary>
    public ApplyProfileResult ApplyTemplateProfile(
        string existingTerraform,
        ApimTemplateProfile? profile,
        ApplyProfileOptions? options = null,
        IReadOnlyDictionary<string, string>? variableValues = null,
        bool resolve = false) =>
        _syncOrchestrator.ApplyProfile(existingTerraform, profile, options ?? new ApplyProfileOptions(), variableValues, resolve);

    /// <summary>Generates a standalone APIM product Terraform block.</summary>
    public ProductGenerationResult GenerateProduct(ApimProductRequest? request = null) =>
        _productGenerator.Generate(request ?? new ApimProductRequest());

    /// <summary>Transforms a Terraform config from one environment to another.</summary>
    public EnvironmentTransformResult TransformEnvironment(
        string sourceTerraform,
        EnvironmentTransformSettings settings,
        string? existingTargetTerraform = null) =>
        _environmentTransformer.Transform(sourceTerraform, settings, existingTargetTerraform);

    /// <summary>Lists the operations of an OpenAPI spec in the unified format.</summary>
    public OperationsListResult ListOpenApiOperations(string openApiJson, string sourceUrl = "inline") =>
        _openApiOperationsFetcher.ParseOperations(openApiJson, sourceUrl);

    /// <summary>Lists the operations of a Terraform config in the unified format.</summary>
    public OperationsListResult ParseTerraformOperations(string terraform) =>
        _terraformOperationsParser.Parse(terraform);
}
