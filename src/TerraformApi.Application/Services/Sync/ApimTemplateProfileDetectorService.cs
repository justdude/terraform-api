using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Detects the templating style of an existing Terraform file
/// (§REV-1.3): per-field templated/literal counts, the global variable
/// dictionary, a confidence rating, and an inferred profile that can be used
/// to generate new operations in the same style.
/// </summary>
public sealed class ApimTemplateProfileDetectorService : IApimTemplateProfileDetector
{
    private static readonly string[] ApiFields =
    [
        "apim_resource_group_name", "apim_name", "name", "display_name",
        "path", "service_url", "revision", "product_id", "subscription_required"
    ];

    private static readonly string[] OperationFields =
    [
        "operation_id", "apim_resource_group_name", "apim_name", "api_name",
        "display_name", "method", "url_template", "status_code", "description"
    ];

    /// <inheritdoc />
    public DetectedProfile Detect(ParsedApimDocument document)
    {
        var fields = new Dictionary<string, DetectedFieldAccumulator>();
        var allVariables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in document.ApiGroups)
        {
            foreach (var api in group.Apis)
            {
                foreach (var fieldName in ApiFields)
                    Track(fields, "api." + fieldName, api.AstNode.Get(fieldName), allVariables);
            }

            foreach (var op in group.Operations)
            {
                foreach (var fieldName in OperationFields)
                    Track(fields, "api_operation." + fieldName, op.AstNode.Get(fieldName), allVariables);
            }

            // Policy heredocs also reference variables (CORS origins etc.).
            foreach (var api in group.Apis)
            {
                if (api.AstNode.Get("policy") is HclHeredoc heredoc)
                {
                    foreach (var name in Hcl.HclParserService.ExtractReferences(heredoc.Content))
                        allVariables.Add(name);
                }
            }
        }

        var totalTemplated = fields.Values.Sum(f => f.TemplatedOccurrences);
        var totalLiteral = fields.Values.Sum(f => f.LiteralOccurrences);
        var total = totalTemplated + totalLiteral;

        var confidence = total == 0
            ? StylingConfidence.Empty
            : (double)totalTemplated / total > 0.7
                ? StylingConfidence.HighlyTemplated
                : (double)totalTemplated / total > 0.3
                    ? StylingConfidence.Mixed
                    : StylingConfidence.MostlyLiteral;

        var inferredProfile = BuildInferredProfile(fields);
        var closestKnown = MatchToKnownProfile(inferredProfile, confidence);

        return new DetectedProfile
        {
            InferredProfile = inferredProfile,
            DetectedFields = fields.Values.Select(f => f.ToDetectedField()).ToList(),
            AllReferencedVariables = allVariables,
            LiteralValuesByField = fields
                .Where(kv => kv.Value.ObservedLiterals.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ObservedLiterals.Distinct().ToList()),
            Confidence = confidence,
            ClosestKnownProfileName = closestKnown
        };
    }

    private static void Track(
        Dictionary<string, DetectedFieldAccumulator> fields,
        string fieldPath,
        HclValue? value,
        HashSet<string> allVariables)
    {
        if (value is null)
            return;

        if (!fields.TryGetValue(fieldPath, out var acc))
        {
            acc = new DetectedFieldAccumulator { FieldPath = fieldPath };
            fields[fieldPath] = acc;
        }

        switch (value)
        {
            case HclInterpolation interpolation:
                acc.TemplatedOccurrences++;
                acc.ObservedExpressions.Add(interpolation.InnerText);
                foreach (var name in interpolation.ReferencedExpressions)
                    allVariables.Add(name);
                break;

            case HclLiteral { Kind: HclLiteralKind.String } literal:
                acc.LiteralOccurrences++;
                acc.ObservedLiterals.Add(literal.RawValue);
                break;

            case HclLiteral literal:
                acc.LiteralOccurrences++;
                acc.ObservedLiterals.Add(literal.RawValue);
                break;
        }
    }

    /// <summary>
    /// For each field: when one expression accounts for more than half of the
    /// non-empty observations, it becomes the field's template in the inferred
    /// profile. Fields that are always literal are NOT included — new operations
    /// will also use literals for them.
    /// </summary>
    private static ApimTemplateProfile BuildInferredProfile(
        Dictionary<string, DetectedFieldAccumulator> fields)
    {
        var apiTemplates = new Dictionary<string, string>();
        var opTemplates = new Dictionary<string, string>();

        foreach (var (path, acc) in fields)
        {
            var dominant = DominantExpression(acc);
            if (dominant is null)
                continue;

            var fieldName = path[(path.IndexOf('.') + 1)..];
            if (path.StartsWith("api.", StringComparison.Ordinal))
                apiTemplates[fieldName] = dominant;
            else
                opTemplates[fieldName] = dominant;
        }

        return new ApimTemplateProfile
        {
            Name = "InferredProfile",
            ApiFieldTemplates = apiTemplates,
            OperationFieldTemplates = opTemplates,
            OperationIdTemplate = opTemplates.GetValueOrDefault("operation_id")
        };
    }

    private static string? DominantExpression(DetectedFieldAccumulator acc)
    {
        if (acc.ObservedExpressions.Count == 0)
            return null;

        var total = acc.TemplatedOccurrences + acc.LiteralOccurrences;
        var (expression, count) = acc.ObservedExpressions
            .GroupBy(e => e)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(g => g.Item2)
            .First();

        return count * 2 > total ? expression : null;
    }

    private static string? MatchToKnownProfile(ApimTemplateProfile inferred, StylingConfidence confidence)
    {
        if (confidence == StylingConfidence.Empty)
            return null;

        if (confidence == StylingConfidence.MostlyLiteral)
            return ApimTemplateProfile.LiteralProfile.Name;

        var candidates = new[] { ApimTemplateProfile.UserExampleProfile, ApimTemplateProfile.ExtendedProfile };
        var best = candidates
            .Select(p => (Profile: p, Score: SimilarityScore(inferred, p)))
            .OrderByDescending(x => x.Score)
            .First();

        return best.Score > 0 ? best.Profile.Name : null;
    }

    private static int SimilarityScore(ApimTemplateProfile inferred, ApimTemplateProfile known)
    {
        var score = 0;
        foreach (var (field, template) in known.ApiFieldTemplates)
        {
            if (inferred.ApiFieldTemplates.TryGetValue(field, out var observed) && observed == template)
                score++;
        }
        foreach (var (field, template) in known.OperationFieldTemplates)
        {
            if (inferred.OperationFieldTemplates.TryGetValue(field, out var observed) && observed == template)
                score++;
        }
        return score;
    }

    private sealed class DetectedFieldAccumulator
    {
        public required string FieldPath { get; init; }
        public int TemplatedOccurrences { get; set; }
        public int LiteralOccurrences { get; set; }
        public List<string> ObservedExpressions { get; } = [];
        public List<string> ObservedLiterals { get; } = [];

        public DetectedField ToDetectedField() => new()
        {
            FieldPath = FieldPath,
            TemplatedOccurrences = TemplatedOccurrences,
            LiteralOccurrences = LiteralOccurrences,
            ObservedExpressions = ObservedExpressions.Distinct().ToList(),
            ObservedLiterals = ObservedLiterals.Distinct().ToList()
        };
    }
}
