using TerraformApi.Application.Services.Apim;
using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Templatize ↔ Resolve conversions (§REV-1.2.5). Apply replaces literal
/// values with the profile's interpolations; Resolve substitutes variable
/// values into interpolations, producing literals.
/// </summary>
public sealed class ApimTemplateProfileApplierService : IApimTemplateProfileApplier
{
    private readonly TerraformInterpolationResolver _resolver;

    public ApimTemplateProfileApplierService(TerraformInterpolationResolver resolver) =>
        _resolver = resolver;

    /// <inheritdoc />
    public List<string> Apply(
        ParsedApimDocument document,
        ApimTemplateProfile profile,
        ApplyProfileOptions options)
    {
        var changes = new List<string>();

        foreach (var group in document.ApiGroups)
        {
            foreach (var api in group.Apis)
            {
                foreach (var (field, template) in profile.ApiFieldTemplates)
                    ApplyToField(api.AstNode, "api." + field, field, template, options, changes);
            }

            foreach (var op in group.Operations)
            {
                var kebabId = (op.OperationId.Node as HclLiteral)?.RawValue is { } literalId
                    ? ApimTerraformWriterService.ToKebabCase(literalId)
                    : "";

                foreach (var (field, template) in profile.OperationFieldTemplates)
                {
                    var resolvedTemplate = ApimTerraformWriterService.ApplyOpSubstitution(template, kebabId);
                    ApplyToField(op.AstNode, "api_operation." + field, field, resolvedTemplate, options, changes);
                }
            }
        }

        return changes;
    }

    private static void ApplyToField(
        HclObject node,
        string fieldPath,
        string field,
        string template,
        ApplyProfileOptions options,
        List<string> changes)
    {
        var existing = node.Get(field);

        switch (existing)
        {
            case HclInterpolation:
                return; // already templated — nothing to do

            case null:
            case HclLiteral { Kind: HclLiteralKind.String, RawValue: "" }:
            case HclLiteral { Kind: HclLiteralKind.Null }:
                ReplaceField(node, field, template);
                changes.Add($"{fieldPath}: (empty) → {template}");
                return;

            case HclLiteral literal when options.OverwriteExisting:
                ReplaceField(node, field, template);
                changes.Add($"{fieldPath}: \"{literal.RawValue}\" → {template}");
                return;

            default:
                return; // literal with a value and OverwriteExisting=false — keep
        }
    }

    private static void ReplaceField(HclObject node, string field, string template)
    {
        var newAssignment = new HclAssignment
        {
            Key = field,
            Value = new HclInterpolation
            {
                InnerText = template,
                ReferencedExpressions = HclParserService.ExtractReferences(template)
            }
        };

        var index = node.Items.FindIndex(i => i is HclAssignment a && a.Key == field);
        if (index >= 0)
            node.Items[index] = newAssignment;
        else
            node.Items.Add(newAssignment);
    }

    /// <inheritdoc />
    public List<string> Resolve(
        ParsedApimDocument document,
        IReadOnlyDictionary<string, string> variableValues,
        List<string> warnings)
    {
        var changes = new List<string>();

        foreach (var group in document.ApiGroups)
        {
            foreach (var api in group.Apis)
                ResolveObject(api.AstNode, "api", variableValues, changes, warnings);

            foreach (var op in group.Operations)
                ResolveObject(op.AstNode, "api_operation", variableValues, changes, warnings);
        }

        return changes;
    }

    private void ResolveObject(
        HclObject node,
        string pathPrefix,
        IReadOnlyDictionary<string, string> variables,
        List<string> changes,
        List<string> warnings)
    {
        for (var i = 0; i < node.Items.Count; i++)
        {
            if (node.Items[i] is not HclAssignment { Value: HclInterpolation interpolation } assignment)
                continue;

            var result = _resolver.ResolveWithReport(interpolation.InnerText, variables);

            if (result.HasUnresolvedExpressions)
            {
                foreach (var expr in result.UnresolvedExpressions)
                    warnings.Add($"{pathPrefix}.{assignment.Key}: variable '{expr}' has no value — left as ${{{expr}}}");
            }

            if (result.Value == interpolation.InnerText)
                continue; // nothing was substituted

            HclValue newValue = result.HasUnresolvedExpressions
                ? new HclInterpolation
                {
                    InnerText = result.Value,
                    ReferencedExpressions = HclParserService.ExtractReferences(result.Value)
                }
                : new HclLiteral { RawValue = result.Value, Kind = HclLiteralKind.String };

            node.Items[i] = new HclAssignment
            {
                Key = assignment.Key,
                KeyIsQuoted = assignment.KeyIsQuoted,
                Value = newValue
            };
            changes.Add($"{pathPrefix}.{assignment.Key}: \"{interpolation.InnerText}\" → \"{result.Value}\"");
        }
    }
}
