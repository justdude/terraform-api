using Microsoft.Extensions.Logging;
using TerraformApi.Application.Services.Apim;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// The append-only sync engine (§4.6). Applies <see cref="MergePolicy"/> to the
/// AST of an existing Terraform document:
/// operations only in OpenAPI are appended (in the detected templating style,
/// with leading comments), operations only in Terraform are preserved, matched
/// operations are enriched only where policy allows and the field is missing.
/// Nothing is ever deleted.
/// </summary>
public sealed class AppendOnlySynchronizerService : IAppendOnlySynchronizer
{
    private readonly IOperationMatcher _matcher;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly IApimTemplateProfileDetector _profileDetector;
    private readonly IOperationCommentBuilder _commentBuilder;
    private readonly IHclWriter _hclWriter;
    private readonly IApimTerraformWriter _apimWriter;
    private readonly TerraformInterpolationResolver _resolver;
    private readonly ILogger<AppendOnlySynchronizerService> _logger;

    public AppendOnlySynchronizerService(
        IOperationMatcher matcher,
        IDuplicateDetector duplicateDetector,
        IApimTemplateProfileDetector profileDetector,
        IOperationCommentBuilder commentBuilder,
        IHclWriter hclWriter,
        IApimTerraformWriter apimWriter,
        TerraformInterpolationResolver resolver,
        ILogger<AppendOnlySynchronizerService> logger)
    {
        _matcher = matcher;
        _duplicateDetector = duplicateDetector;
        _profileDetector = profileDetector;
        _commentBuilder = commentBuilder;
        _hclWriter = hclWriter;
        _apimWriter = apimWriter;
        _resolver = resolver;
        _logger = logger;
    }

    /// <inheritdoc />
    public SyncResult Synchronize(
        ParsedApimDocument existingParsed,
        ApimConfiguration newConfiguration,
        MergePolicy policy,
        OperationMatchStrategy matchStrategy,
        SyncOptions? options = null)
    {
        options ??= new SyncOptions();

        var duplicates = _duplicateDetector.Detect(existingParsed, matchStrategy);
        var detected = _profileDetector.Detect(existingParsed);
        var effectiveProfile = options.OverrideProfile
            ?? (detected.Confidence == StylingConfidence.Empty
                ? ApimTemplateProfile.UserExampleProfile
                : detected.InferredProfile);

        var warnings = new List<SyncWarning>();
        foreach (var duplicate in duplicates)
        {
            warnings.Add(new SyncWarning
            {
                Message = $"Duplicate by {duplicate.MatchedBy}: '{duplicate.MatchedValue}' ({duplicate.Members.Count} operations)",
                Kind = SyncWarningKind.DuplicateDetected
            });
        }

        // ---- Locate or create the target group --------------------------------
        var targetGroup = FindTargetGroup(existingParsed, newConfiguration, matchStrategy);
        if (targetGroup is null)
        {
            targetGroup = CreateNewGroup(existingParsed, newConfiguration, effectiveProfile, options);
            _logger.LogInformation("Created new api group '{GroupName}'", newConfiguration.ApiGroupName);
        }

        var targetOperations = SelectTargetOperations(existingParsed, targetGroup, newConfiguration, matchStrategy);

        // Interpolation warnings for existing operations.
        foreach (var op in targetOperations)
        {
            if (op.OperationId.IsInterpolated)
            {
                warnings.Add(new SyncWarning
                {
                    Message = $"operation_id '{op.OperationId.StructuralText}' contains interpolation — matching is structural",
                    OperationId = op.OperationId.StructuralText,
                    Kind = SyncWarningKind.OperationIdContainsInterpolation
                });
            }
            if (op.UrlTemplate.IsInterpolated)
            {
                warnings.Add(new SyncWarning
                {
                    Message = $"url_template '{op.UrlTemplate.StructuralText}' contains interpolation",
                    OperationId = op.OperationId.StructuralText,
                    Kind = SyncWarningKind.UrlTemplateContainsInterpolation
                });
            }
        }

        // ---- Match ------------------------------------------------------------
        var tfFingerprints = targetOperations
            .Select((op, i) => _matcher.FingerprintFromTerraform(op, matchStrategy) with { SourceIndex = i })
            .ToList();
        var openApiFingerprints = newConfiguration.ApiOperations
            .Select((op, i) => _matcher.FingerprintFromOpenApi(op, matchStrategy) with { SourceIndex = i })
            .ToList();

        var matchResult = _matcher.Match(openApiFingerprints, tfFingerprints, matchStrategy);

        foreach (var ambiguity in matchResult.Ambiguities)
        {
            warnings.Add(new SyncWarning
            {
                Message = $"Ambiguous match for '{ambiguity.Source.Method} {ambiguity.Source.UrlTemplate}' " +
                          $"on key {ambiguity.AmbiguousOnKey}: {ambiguity.Candidates.Count} candidates — nothing applied",
                OperationId = ambiguity.Source.OperationId,
                Kind = SyncWarningKind.AmbiguousMatch
            });
        }

        var diffs = new List<OperationDiff>();
        var added = 0;
        var preserved = 0;
        var enriched = 0;
        var identical = 0;
        var newPlaceholders = new SortedSet<string>(StringComparer.Ordinal);

        // ---- 1. Operations only in OpenAPI: append ----------------------------
        if (policy.NewOperationPolicy == NewOperationMode.Append)
        {
            var operationsArray = GetOrCreateOperationsArray(targetGroup);

            foreach (var openApiFp in matchResult.OnlyInOpenApi)
            {
                var sourceOp = newConfiguration.ApiOperations[openApiFp.SourceIndex];
                var node = ApimTerraformWriterService.BuildOperationObject(sourceOp, effectiveProfile);
                var placeholders = _commentBuilder.ExtractPlaceholders(node);
                foreach (var placeholder in placeholders)
                    newPlaceholders.Add(placeholder);

                var leading = new List<HclComment>();
                if (options.AddOperationComments)
                {
                    var opIdText = (node.Get("operation_id") as HclInterpolation)?.InnerText
                                   ?? (node.Get("operation_id") as HclLiteral)?.RawValue
                                   ?? sourceOp.OperationId;
                    leading = _commentBuilder.Build(new OperationCommentSpec
                    {
                        Method = sourceOp.Method,
                        UrlTemplate = sourceOp.UrlTemplate,
                        OperationId = opIdText,
                        DisplayName = sourceOp.DisplayName,
                        Source = OperationCommentSource.OpenApi,
                        PlaceholdersToReplace = placeholders
                    });
                }

                operationsArray.Items.Add(new HclArrayItem { LeadingComments = leading, Value = node });
                _logger.LogInformation("Appended operation {Method} {Url} (op_id {OperationId})",
                    sourceOp.Method, sourceOp.UrlTemplate, sourceOp.OperationId);

                diffs.Add(new OperationDiff
                {
                    OpenApiFingerprint = openApiFp,
                    Kind = OperationDiffKind.AddedFromOpenApi,
                    AppliedChanges = [$"appended {sourceOp.Method} {sourceOp.UrlTemplate}"]
                });
                added++;
            }
        }
        else
        {
            foreach (var openApiFp in matchResult.OnlyInOpenApi)
            {
                diffs.Add(new OperationDiff
                {
                    OpenApiFingerprint = openApiFp,
                    Kind = OperationDiffKind.AddedFromOpenApi,
                    SkippedDueToPolicy = ["new operation not appended (NewOperationPolicy=ReportOnly)"]
                });
            }
        }

        // ---- 2. Operations only in Terraform: preserve -------------------------
        const string deprecationMarker = "[deprecated: not present in OpenAPI spec]";
        foreach (var tfFp in matchResult.OnlyInTerraform)
        {
            var appliedChanges = new List<string>();

            // MarkDeprecated: annotate the description without deleting anything.
            // (Remove is intentionally NOT honored — append-only invariant.)
            if (policy.UnknownOperationPolicy == OperationPreservationMode.MarkDeprecated
                && tfFp.SourceIndex >= 0)
            {
                var tfOp = targetOperations[tfFp.SourceIndex];
                var currentDescription = (tfOp.AstNode.Get("description") as HclLiteral)?.RawValue ?? "";
                if (!currentDescription.Contains(deprecationMarker, StringComparison.Ordinal))
                {
                    var annotated = string.IsNullOrEmpty(currentDescription)
                        ? deprecationMarker
                        : $"{currentDescription} {deprecationMarker}";
                    WriteScalarIntoAst(tfOp.AstNode, "description", annotated);
                    appliedChanges.Add("description += deprecation marker");
                    _logger.LogInformation("Marked operation {OperationId} as deprecated", tfFp.OperationId);
                }
            }
            else if (policy.UnknownOperationPolicy == OperationPreservationMode.Remove)
            {
                warnings.Add(new SyncWarning
                {
                    Message = "UnknownOperationPolicy=Remove is not supported by append-only sync — operation preserved",
                    OperationId = tfFp.OperationId,
                    Kind = SyncWarningKind.SkippedFieldDueToPolicy
                });
            }

            diffs.Add(new OperationDiff
            {
                TerraformFingerprint = tfFp,
                Kind = OperationDiffKind.PreservedFromTerraform,
                AppliedChanges = appliedChanges
            });
            preserved++;
        }

        // ---- 3. Matched: enrichment by policy ----------------------------------
        foreach (var (tfFp, openApiFp) in matchResult.Matched)
        {
            var tfOp = targetOperations[tfFp.SourceIndex];
            var openApiOp = newConfiguration.ApiOperations[openApiFp.SourceIndex];

            var (fieldDiffs, applied, skipped) = EnrichOperation(tfOp, openApiOp, policy);

            var kind = applied.Count > 0 ? OperationDiffKind.Changed : OperationDiffKind.Identical;
            diffs.Add(new OperationDiff
            {
                TerraformFingerprint = tfFp,
                OpenApiFingerprint = openApiFp,
                Kind = kind,
                FieldDiffs = fieldDiffs,
                AppliedChanges = applied,
                SkippedDueToPolicy = skipped
            });

            foreach (var skip in skipped)
            {
                _logger.LogInformation("Skipped field {Field} on operation {OperationId} due to policy",
                    skip, tfFp.OperationId);
                warnings.Add(new SyncWarning
                {
                    Message = $"Skipped '{skip}' due to policy on operation '{tfFp.OperationId}'",
                    OperationId = tfFp.OperationId,
                    Kind = SyncWarningKind.SkippedFieldDueToPolicy
                });
            }

            if (kind == OperationDiffKind.Changed)
                enriched++;
            else
                identical++;
        }

        // ---- REPLACE BEFORE APPLY header ---------------------------------------
        if (options.AddReplaceBeforeApplyHeader && added > 0 && newPlaceholders.Count > 0)
            EnsureReplaceBeforeApplyHeader(targetGroup, newPlaceholders);

        var finalHcl = _hclWriter.Write(existingParsed.Ast);

        var report = new SyncReport
        {
            GeneratedAt = DateTime.UtcNow,
            ApiGroupName = newConfiguration.ApiGroupName,
            TotalOperationsInTerraform = targetOperations.Count,
            TotalOperationsInOpenApi = newConfiguration.ApiOperations.Count,
            OperationsAdded = added,
            OperationsPreserved = preserved,
            OperationsEnriched = enriched,
            OperationsIdentical = identical,
            Diffs = diffs,
            Duplicates = duplicates,
            Warnings = warnings
        };

        return new SyncResult
        {
            Success = true,
            TerraformConfig = finalHcl,
            Report = report
        };
    }

    // -----------------------------------------------------------------
    // Target selection
    // -----------------------------------------------------------------

    private ParsedApiGroup? FindTargetGroup(
        ParsedApimDocument parsed, ApimConfiguration config, OperationMatchStrategy strategy)
    {
        // Exact group-name match first.
        var byName = parsed.ApiGroups.FirstOrDefault(g => g.ApiGroupName == config.ApiGroupName);
        if (byName is not null)
            return byName;

        // (rg, api_name) bundle match — resolves file-side interpolations when a
        // variable context is available.
        var targetKey = new ApimApiGroupKey
        {
            ApimResourceGroupNameRaw = config.Api.ApimResourceGroupName,
            ApiNameRaw = config.Api.Name
        };

        foreach (var (key, bundle) in parsed.ApisByGroupKey)
        {
            var candidate = strategy.VariableContext is { } vars
                ? key with
                {
                    ApimResourceGroupNameResolved = _resolver.Resolve(key.ApimResourceGroupNameRaw, vars),
                    ApiNameResolved = _resolver.Resolve(key.ApiNameRaw, vars)
                }
                : key;

            if (candidate.Equals(targetKey) && bundle.OwnerGroup is not null)
                return bundle.OwnerGroup;
        }

        // Single-group fallback: with one group whose identity is templated
        // (and no variable context to resolve it), it is the only sensible
        // target (UX invariant — sync the user example without configuration).
        // A literal identity that simply doesn't match means a new group.
        if (parsed.ApiGroups.Count == 1 && IsIdentityAmbiguous(parsed.ApiGroups[0]))
            return parsed.ApiGroups[0];

        return null;
    }

    /// <summary>True when the group's identifying names contain interpolations.</summary>
    private static bool IsIdentityAmbiguous(ParsedApiGroup group)
    {
        if (group.ApiGroupName.Contains("${"))
            return true;

        var identityTexts = group.Apis
            .SelectMany(a => new[] { a.ApimResourceGroupName.StructuralText, a.Name.StructuralText })
            .Concat(group.Operations.SelectMany(o => new[]
            {
                o.ApimResourceGroupName?.StructuralText,
                o.ApiName?.StructuralText
            }));

        return identityTexts.Any(t => t?.Contains("${") == true);
    }

    private static List<ParsedApiOperation> SelectTargetOperations(
        ParsedApimDocument parsed,
        ParsedApiGroup group,
        ApimConfiguration config,
        OperationMatchStrategy strategy)
    {
        // Prefer bundle scoping when several APIs live in the same group.
        var distinctKeys = group.Operations
            .Select(op => (Rg: op.ApimResourceGroupName?.StructuralText, Api: op.ApiName?.StructuralText))
            .Distinct()
            .Count();

        if (distinctKeys <= 1)
            return group.Operations;

        var targetKey = new ApimApiGroupKey
        {
            ApimResourceGroupNameRaw = config.Api.ApimResourceGroupName,
            ApiNameRaw = config.Api.Name
        };

        if (parsed.ApisByGroupKey.TryGetValue(targetKey, out var bundle))
            return bundle.Operations.Where(op => group.Operations.Contains(op)).ToList();

        return group.Operations;
    }

    // -----------------------------------------------------------------
    // Group / array creation
    // -----------------------------------------------------------------

    private ParsedApiGroup CreateNewGroup(
        ParsedApimDocument parsed,
        ApimConfiguration config,
        ApimTemplateProfile profile,
        SyncOptions options)
    {
        // Build a fresh group object (flat) using the profile, then graft it
        // into the existing AST under the detected (or default) parent path.
        var groupObject = BuildGroupObject(config, profile, options);
        var groupAssignment = new HclAssignment
        {
            Key = config.ApiGroupName,
            KeyIsQuoted = ApimTerraformWriterService.NeedsQuotedKey(config.ApiGroupName),
            Value = groupObject
        };

        var parentPath = parsed.ApiGroupParentPath ?? (parsed.ApiGroups.Count > 0 ? null : ["apis", "bpc_apis", "backend_apis"]);
        var parent = NavigateOrCreateParent(parsed.Ast, parentPath ?? []);
        parent.Add(groupAssignment);

        return new ParsedApiGroup
        {
            ApiGroupName = config.ApiGroupName,
            KeyIsQuoted = groupAssignment.KeyIsQuoted,
            AstNode = groupObject
        };
    }

    private HclObject BuildGroupObject(ApimConfiguration config, ApimTemplateProfile profile, SyncOptions options)
    {
        // Reuse the writer's builder by constructing a flat document and taking
        // the group object out of it.
        var writerBuildOptions = new BuildOptions
        {
            Profile = profile,
            ApiGroupParentPath = [],
            AddOperationComments = options.AddOperationComments,
            AddReplaceBeforeApplyHeader = options.AddReplaceBeforeApplyHeader
        };

        var emptyOperationsConfig = config with { ApiOperations = [] };
        var document = _apimWriter.BuildFromConfiguration(emptyOperationsConfig, writerBuildOptions);
        return document.ApiGroups.Single().AstNode;
    }

    private static List<HclObjectItem> NavigateOrCreateParent(HclDocument document, IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return document.RootItems;

        var currentItems = document.RootItems;
        foreach (var key in path)
        {
            var existing = currentItems.OfType<HclAssignment>().FirstOrDefault(a => a.Key == key);
            if (existing?.Value is HclObject obj)
            {
                currentItems = obj.Items;
            }
            else
            {
                var newObject = new HclObject();
                currentItems.Add(new HclAssignment { Key = key, Value = newObject });
                currentItems = newObject.Items;
            }
        }
        return currentItems;
    }

    private static HclArray GetOrCreateOperationsArray(ParsedApiGroup group)
    {
        if (group.AstNode.Get("api_operations") is HclArray existing)
            return existing;

        var array = new HclArray();
        group.AstNode.Items.Add(new HclAssignment { Key = "api_operations", Value = array });
        return array;
    }

    // -----------------------------------------------------------------
    // Enrichment
    // -----------------------------------------------------------------

    /// <summary>Scalar fields eligible for diffing, with their OpenAPI values.</summary>
    private static IEnumerable<(string Field, string? OpenApiValue)> ScalarFields(ApimApiOperation op)
    {
        yield return ("operation_id", op.OperationId);
        yield return ("method", op.Method.ToUpperInvariant());
        yield return ("url_template", op.UrlTemplate);
        yield return ("display_name", op.DisplayName);
        yield return ("description", op.Description);
        yield return ("status_code", op.StatusCode.ToString());
    }

    private (List<FieldDiff> FieldDiffs, List<string> Applied, List<string> Skipped) EnrichOperation(
        ParsedApiOperation tfOp, ApimApiOperation openApiOp, MergePolicy policy)
    {
        var fieldDiffs = new List<FieldDiff>();
        var applied = new List<string>();
        var skipped = new List<string>();

        foreach (var (field, openApiValue) in ScalarFields(openApiOp))
        {
            var tfValue = (tfOp.AstNode.Get(field) as HclLiteral)?.RawValue
                          ?? (tfOp.AstNode.Get(field) as HclInterpolation)?.InnerText;

            var fieldPolicy = policy.OperationFieldPolicies.GetValueOrDefault(field, FieldMergePolicy.Preserve);
            var isMissing = IsFieldMissing(tfOp.AstNode, field);
            var differs = !string.Equals(tfValue ?? "", openApiValue ?? "", StringComparison.Ordinal);

            if (!differs && !isMissing)
            {
                fieldDiffs.Add(new FieldDiff
                {
                    FieldPath = field,
                    TerraformValue = tfValue,
                    OpenApiValue = openApiValue,
                    Outcome = FieldDiffOutcome.NoChange
                });
                continue;
            }

            switch (fieldPolicy)
            {
                case FieldMergePolicy.Preserve:
                    fieldDiffs.Add(new FieldDiff
                    {
                        FieldPath = field,
                        TerraformValue = tfValue,
                        OpenApiValue = openApiValue,
                        Outcome = FieldDiffOutcome.SkippedPreserve
                    });
                    skipped.Add(field);
                    break;

                case FieldMergePolicy.EnrichIfMissing when isMissing && !string.IsNullOrEmpty(openApiValue):
                    WriteScalarIntoAst(tfOp.AstNode, field, openApiValue);
                    fieldDiffs.Add(new FieldDiff
                    {
                        FieldPath = field,
                        TerraformValue = tfValue,
                        OpenApiValue = openApiValue,
                        Outcome = FieldDiffOutcome.AppliedEnrichIfMissing
                    });
                    applied.Add(field);
                    _logger.LogInformation("Enriched operation {OperationId}: field {Field} set to {Value}",
                        tfOp.OperationId.StructuralText, field, openApiValue);
                    break;

                case FieldMergePolicy.EnrichIfMissing:
                    fieldDiffs.Add(new FieldDiff
                    {
                        FieldPath = field,
                        TerraformValue = tfValue,
                        OpenApiValue = openApiValue,
                        Outcome = FieldDiffOutcome.SkippedPreserve
                    });
                    skipped.Add(field);
                    break;

                case FieldMergePolicy.Overwrite when !string.IsNullOrEmpty(openApiValue):
                    WriteScalarIntoAst(tfOp.AstNode, field, openApiValue);
                    fieldDiffs.Add(new FieldDiff
                    {
                        FieldPath = field,
                        TerraformValue = tfValue,
                        OpenApiValue = openApiValue,
                        Outcome = FieldDiffOutcome.AppliedOverwrite
                    });
                    applied.Add(field);
                    _logger.LogInformation("Overwrote operation {OperationId}: field {Field} set to {Value}",
                        tfOp.OperationId.StructuralText, field, openApiValue);
                    break;
            }
        }

        EnrichCollections(tfOp, openApiOp, policy, applied, fieldDiffs);

        return (fieldDiffs, applied, skipped);
    }

    /// <summary>A field is missing when absent, or an empty string literal.</summary>
    internal static bool IsFieldMissing(HclObject node, string field)
    {
        var value = node.Get(field);
        return value switch
        {
            null => true,
            HclLiteral { Kind: HclLiteralKind.String, RawValue: "" } => true,
            HclLiteral { Kind: HclLiteralKind.Null } => true,
            _ => false
        };
    }

    private static void WriteScalarIntoAst(HclObject node, string field, string value)
    {
        var newAssignment = new HclAssignment
        {
            Key = field,
            Value = new HclLiteral { RawValue = value, Kind = HclLiteralKind.String }
        };

        var existingIndex = node.Items.FindIndex(i => i is HclAssignment a && a.Key == field);
        if (existingIndex >= 0)
            node.Items[existingIndex] = newAssignment;
        else
            node.Items.Add(newAssignment);
    }

    // -----------------------------------------------------------------
    // Collection enrichment (request.header / request.query / responses)
    // -----------------------------------------------------------------

    private void EnrichCollections(
        ParsedApiOperation tfOp,
        ApimApiOperation openApiOp,
        MergePolicy policy,
        List<string> applied,
        List<FieldDiff> fieldDiffs)
    {
        var openApiHeaders = openApiOp.Requests.SelectMany(r => r.Headers).ToList();
        var openApiQueries = openApiOp.Requests.SelectMany(r => r.QueryParameters).ToList();

        if (openApiHeaders.Count > 0 &&
            policy.CollectionPolicies.GetValueOrDefault("request.header") is CollectionMergePolicy.AppendMissing or CollectionMergePolicy.AppendAndEnrich)
        {
            AppendMissingParameters(tfOp, "header", openApiHeaders, applied, fieldDiffs);
        }

        if (openApiQueries.Count > 0 &&
            policy.CollectionPolicies.GetValueOrDefault("request.query") is CollectionMergePolicy.AppendMissing or CollectionMergePolicy.AppendAndEnrich)
        {
            AppendMissingParameters(tfOp, "query_parameter", openApiQueries, applied, fieldDiffs);
        }

        if (openApiOp.Responses.Count > 0 &&
            policy.CollectionPolicies.GetValueOrDefault("responses") is CollectionMergePolicy.AppendMissing or CollectionMergePolicy.AppendAndEnrich)
        {
            AppendMissingResponses(tfOp, openApiOp.Responses, applied, fieldDiffs);
        }
    }

    private void AppendMissingParameters(
        ParsedApiOperation tfOp,
        string parameterArrayKey,
        List<ApimParameter> openApiParameters,
        List<string> applied,
        List<FieldDiff> fieldDiffs)
    {
        var requestArray = GetOrCreateRequestArray(tfOp);
        var requestObject = GetOrCreateFirstObject(requestArray);

        if (requestObject.Get(parameterArrayKey) is not HclArray parameterArray)
        {
            parameterArray = new HclArray();
            requestObject.Items.Add(new HclAssignment { Key = parameterArrayKey, Value = parameterArray });
        }

        var existingNames = parameterArray.Items
            .Select(i => ((i.Value as HclObject)?.Get("name") as HclLiteral)?.RawValue)
            .Where(n => n is not null)
            .Select(n => n!.ToLowerInvariant())
            .ToHashSet();

        foreach (var parameter in openApiParameters)
        {
            if (existingNames.Contains(parameter.Name.ToLowerInvariant()))
                continue;

            parameterArray.Items.Add(new HclArrayItem
            {
                Value = new HclObject
                {
                    Items =
                    [
                        new HclAssignment { Key = "name", Value = Str(parameter.Name) },
                        new HclAssignment { Key = "required", Value = new HclLiteral { RawValue = parameter.Required ? "true" : "false", Kind = HclLiteralKind.Bool } },
                        new HclAssignment { Key = "type", Value = Str(parameter.Type) },
                        new HclAssignment { Key = "description", Value = Str(parameter.Description) }
                    ]
                }
            });

            var collectionPath = parameterArrayKey == "header" ? "request.header" : "request.query";
            applied.Add($"{collectionPath} += {parameter.Name}");
            fieldDiffs.Add(new FieldDiff
            {
                FieldPath = $"{collectionPath}[name={parameter.Name}]",
                TerraformValue = null,
                OpenApiValue = parameter.Name,
                Outcome = FieldDiffOutcome.AppliedCollectionAppend
            });
            _logger.LogInformation("Appended {Path} '{Name}' to operation {OperationId}",
                collectionPath, parameter.Name, tfOp.OperationId.StructuralText);
        }
    }

    private void AppendMissingResponses(
        ParsedApiOperation tfOp,
        List<ApimOperationResponse> openApiResponses,
        List<string> applied,
        List<FieldDiff> fieldDiffs)
    {
        var responseArray = tfOp.ResponsesArray;
        if (responseArray is null)
        {
            if (tfOp.AstNode.Get("response") is HclArray existing)
            {
                responseArray = existing;
            }
            else
            {
                responseArray = new HclArray();
                tfOp.AstNode.Items.Add(new HclAssignment { Key = "response", Value = responseArray });
            }
        }

        var existingCodes = responseArray.Items
            .Select(i => ((i.Value as HclObject)?.Get("status_code") as HclLiteral)?.RawValue)
            .Where(c => c is not null)
            .ToHashSet();

        foreach (var response in openApiResponses)
        {
            var code = response.StatusCode.ToString();
            if (existingCodes.Contains(code))
                continue;

            responseArray.Items.Add(new HclArrayItem
            {
                Value = new HclObject
                {
                    Items =
                    [
                        new HclAssignment { Key = "status_code", Value = new HclLiteral { RawValue = code, Kind = HclLiteralKind.Number } },
                        new HclAssignment { Key = "description", Value = Str(response.Description) }
                    ]
                }
            });

            applied.Add($"responses += {code}");
            fieldDiffs.Add(new FieldDiff
            {
                FieldPath = $"responses[status_code={code}]",
                TerraformValue = null,
                OpenApiValue = code,
                Outcome = FieldDiffOutcome.AppliedCollectionAppend
            });
        }
    }

    private static HclArray GetOrCreateRequestArray(ParsedApiOperation tfOp)
    {
        if (tfOp.RequestArray is not null)
            return tfOp.RequestArray;
        if (tfOp.AstNode.Get("request") is HclArray existing)
            return existing;

        var array = new HclArray();
        tfOp.AstNode.Items.Add(new HclAssignment { Key = "request", Value = array });
        return array;
    }

    private static HclObject GetOrCreateFirstObject(HclArray array)
    {
        var first = array.Items.FirstOrDefault(i => i.Value is HclObject);
        if (first?.Value is HclObject obj)
            return obj;

        var newObject = new HclObject();
        array.Items.Add(new HclArrayItem { Value = newObject });
        return newObject;
    }

    // -----------------------------------------------------------------
    // REPLACE BEFORE APPLY header
    // -----------------------------------------------------------------

    private static void EnsureReplaceBeforeApplyHeader(ParsedApiGroup group, IReadOnlyCollection<string> placeholders)
    {
        // Already present? Detected by the marker substring in any comment.
        var alreadyPresent = group.AstNode.Items
            .OfType<HclComment>()
            .Any(c => c.Text.Contains("REPLACE BEFORE APPLY", StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent)
            return;

        var operationsIndex = group.AstNode.Items.FindIndex(i => i is HclAssignment { Key: "api_operations" });
        if (operationsIndex < 0)
            return;

        var header = ApimTerraformWriterService.BuildReplaceBeforeApplyHeader(placeholders.ToList());
        group.AstNode.Items.InsertRange(operationsIndex, header);
    }

    private static HclLiteral Str(string value) => new() { RawValue = value, Kind = HclLiteralKind.String };
}
