using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services;

/// <summary>
/// High-level entry points for the sync engine. Coordinates the OpenAPI parser,
/// the APIM Terraform reader, the append-only synchronizer, the style detector
/// and the profile applier. Mirrors <see cref="ConversionOrchestratorService"/>
/// for the new AST-based flows.
/// </summary>
public sealed class SyncOrchestratorService : ISyncOrchestrator
{
    private readonly IOpenApiParser _openApiParser;
    private readonly IApimTerraformReader _reader;
    private readonly IApimTerraformWriter _writer;
    private readonly IHclParser _hclParser;
    private readonly IAppendOnlySynchronizer _synchronizer;
    private readonly IApimTemplateProfileDetector _profileDetector;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly IApimTemplateProfileApplier _profileApplier;
    private readonly IOperationExecutionGraphBuilder _graphBuilder;

    public SyncOrchestratorService(
        IOpenApiParser openApiParser,
        IApimTerraformReader reader,
        IApimTerraformWriter writer,
        IHclParser hclParser,
        IAppendOnlySynchronizer synchronizer,
        IApimTemplateProfileDetector profileDetector,
        IDuplicateDetector duplicateDetector,
        IApimTemplateProfileApplier profileApplier,
        IOperationExecutionGraphBuilder graphBuilder)
    {
        _openApiParser = openApiParser;
        _reader = reader;
        _writer = writer;
        _hclParser = hclParser;
        _synchronizer = synchronizer;
        _profileDetector = profileDetector;
        _duplicateDetector = duplicateDetector;
        _profileApplier = profileApplier;
        _graphBuilder = graphBuilder;
    }

    /// <inheritdoc />
    public SyncResult Sync(SyncRequest request)
    {
        try
        {
            // Missing settings become {tag} placeholders; surfaced as warnings
            // so the caller knows which values to replace in the output.
            var (settings, defaultedTags) = Domain.Models.ApimPlaceholders.Normalize(request.Settings);

            var configuration = _openApiParser.Parse(request.OpenApiJson, settings);

            ParsedApimDocument parsed;
            if (string.IsNullOrWhiteSpace(request.ExistingTerraform))
            {
                parsed = new ParsedApimDocument { Ast = new HclDocument() };
            }
            else
            {
                var parseResult = _hclParser.TryParse(request.ExistingTerraform);
                if (!parseResult.IsSuccess)
                {
                    return new SyncResult
                    {
                        Success = false,
                        TerraformConfig = "",
                        Report = EmptyReport(request.Settings.ApiGroupName),
                        Errors = parseResult.Diagnostics
                            .Select(d => $"HCL parse error (line {d.Line}, col {d.Column}): {d.Message}")
                            .ToList()
                    };
                }
                parsed = _reader.Read(parseResult.Document!);
            }

            var result = _synchronizer.Synchronize(
                parsed,
                configuration,
                request.MergePolicy ?? new MergePolicy(),
                request.MatchStrategy ?? new OperationMatchStrategy(),
                request.Options);

            if (!result.Success)
                return result;

            foreach (var tag in defaultedTags)
            {
                result.Report.Warnings.Add(new SyncWarning
                {
                    Message = $"Setting not provided — placeholder tag {tag} used; replace it before applying",
                    Kind = SyncWarningKind.SkippedFieldDueToPolicy
                });
            }

            var graph = _graphBuilder.BuildFromSyncReport(result.Report, configuration.ApiGroupName);
            return result with { ExecutionGraph = graph };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new SyncResult
            {
                Success = false,
                TerraformConfig = "",
                Report = EmptyReport(request.Settings.ApiGroupName),
                Errors = [ex.Message]
            };
        }
    }

    /// <inheritdoc />
    public AnalyzeResult Analyze(string existingTerraform)
    {
        if (string.IsNullOrWhiteSpace(existingTerraform))
            return new AnalyzeResult { Success = false, Errors = ["Terraform content is required."] };

        var parseResult = _hclParser.TryParse(existingTerraform);
        if (!parseResult.IsSuccess)
        {
            return new AnalyzeResult
            {
                Success = false,
                Errors = parseResult.Diagnostics
                    .Select(d => $"HCL parse error (line {d.Line}, col {d.Column}): {d.Message}")
                    .ToList()
            };
        }

        var parsed = _reader.Read(parseResult.Document!);
        var detected = _profileDetector.Detect(parsed);
        var duplicates = _duplicateDetector.Detect(parsed, new OperationMatchStrategy());

        var groups = parsed.ApisByGroupKey
            .Select(kv => new ApiGroupSummary
            {
                ApimResourceGroupName = kv.Key.ApimResourceGroupNameRaw,
                ApiName = kv.Key.ApiNameRaw,
                OperationCount = kv.Value.Operations.Count
            })
            .ToList();

        return new AnalyzeResult
        {
            Success = true,
            DetectedProfile = detected,
            ApiGroups = groups,
            TotalOperations = parsed.ApiGroups.Sum(g => g.Operations.Count),
            Duplicates = duplicates
        };
    }

    /// <inheritdoc />
    public ApplyProfileResult ApplyProfile(
        string existingTerraform,
        ApimTemplateProfile? profile,
        ApplyProfileOptions options,
        IReadOnlyDictionary<string, string>? variableValues = null,
        bool resolve = false)
    {
        if (string.IsNullOrWhiteSpace(existingTerraform))
            return new ApplyProfileResult { Success = false, Errors = ["Terraform content is required."] };

        var parseResult = _hclParser.TryParse(existingTerraform);
        if (!parseResult.IsSuccess)
        {
            return new ApplyProfileResult
            {
                Success = false,
                Errors = parseResult.Diagnostics
                    .Select(d => $"HCL parse error (line {d.Line}, col {d.Column}): {d.Message}")
                    .ToList()
            };
        }

        var parsed = _reader.Read(parseResult.Document!);
        var warnings = new List<string>();
        List<string> changes;

        if (resolve)
        {
            if (variableValues is null or { Count: 0 })
                return new ApplyProfileResult { Success = false, Errors = ["Variable values are required for resolve."] };

            changes = _profileApplier.Resolve(parsed, variableValues, warnings);
        }
        else
        {
            if (profile is null)
                return new ApplyProfileResult { Success = false, Errors = ["A template profile is required for templatize."] };

            changes = _profileApplier.Apply(parsed, profile, options);
        }

        return new ApplyProfileResult
        {
            Success = true,
            TerraformConfig = _writer.Write(parsed),
            AppliedChanges = changes,
            Warnings = warnings
        };
    }

    private static SyncReport EmptyReport(string? apiGroupName) => new()
    {
        GeneratedAt = DateTime.UtcNow,
        ApiGroupName = apiGroupName ?? Domain.Models.ApimPlaceholders.ApiGroupName
    };
}
