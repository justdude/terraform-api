using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Detects duplicate operations inside an existing Terraform document (§4.5).
/// Runs OperationId, MethodAndUrl and ApiAndMethodAndUrl keys over all
/// operations in structural mode; interpolated values are compared verbatim,
/// so two "${prefix}-list-${env}" entries are duplicates.
/// </summary>
public sealed class DuplicateDetectorService : IDuplicateDetector
{
    private static readonly OperationMatchKey[] DetectionKeys =
    [
        OperationMatchKey.OperationId,
        OperationMatchKey.MethodAndUrl,
        OperationMatchKey.ApiAndMethodAndUrl
    ];

    /// <inheritdoc />
    public List<DuplicateGroup> Detect(ParsedApimDocument parsed, OperationMatchStrategy strategy)
    {
        var groups = new List<DuplicateGroup>();

        // (operation, owning group) over the whole document.
        var all = parsed.ApiGroups
            .SelectMany(g => g.Operations.Select(op => (Group: g, Operation: op)))
            .ToList();

        foreach (var key in DetectionKeys)
        {
            var index = new Dictionary<string, List<(ParsedApiGroup Group, ParsedApiOperation Operation)>>(StringComparer.Ordinal);

            foreach (var entry in all)
            {
                var value = KeyValue(entry.Operation, key, strategy);
                if (value is null)
                    continue;
                if (!index.TryGetValue(value, out var list))
                    index[value] = list = [];
                list.Add(entry);
            }

            foreach (var (value, members) in index)
            {
                if (members.Count <= 1)
                    continue;

                // ApiAndMethodAndUrl duplicates are already reported by MethodAndUrl
                // when the member sets coincide; only report when it adds information.
                if (key == OperationMatchKey.ApiAndMethodAndUrl &&
                    groups.Any(g => g.MatchedBy == OperationMatchKey.MethodAndUrl &&
                                    SameMembers(g.Members, members)))
                {
                    continue;
                }

                groups.Add(new DuplicateGroup
                {
                    MatchedBy = key,
                    MatchedValue = value,
                    Members = members.Select(m => new DuplicateMember
                    {
                        OperationId = m.Operation.OperationId.StructuralText ?? "(missing)",
                        ApiGroupName = m.Group.ApiGroupName,
                        ApiName = m.Operation.ApiName?.StructuralText ?? "(missing)",
                        LineInSource = m.Operation.AstNode.Line,
                        Severity = DetermineSeverity(key, members)
                    }).ToList()
                });
            }
        }

        return groups;
    }

    private static bool SameMembers(
        List<DuplicateMember> reported,
        List<(ParsedApiGroup Group, ParsedApiOperation Operation)> candidates)
    {
        if (reported.Count != candidates.Count)
            return false;

        var reportedLines = reported.Select(m => m.LineInSource).OrderBy(l => l);
        var candidateLines = candidates.Select(c => c.Operation.AstNode.Line).OrderBy(l => l);
        return reportedLines.SequenceEqual(candidateLines);
    }

    private static string? KeyValue(ParsedApiOperation operation, OperationMatchKey key, OperationMatchStrategy strategy)
    {
        var method = operation.Method.StructuralText?.ToUpperInvariant();
        var url = operation.UrlTemplate.StructuralText is { } u
            ? OperationMatcherService.NormalizeUrl(u, strategy.UrlNormalization)
            : null;

        return key switch
        {
            OperationMatchKey.OperationId =>
                operation.OperationId.StructuralText is { Length: > 0 } id ? id : null,

            OperationMatchKey.MethodAndUrl =>
                method is null || url is null ? null : $"{method}|{url}",

            OperationMatchKey.ApiAndMethodAndUrl =>
                operation.ApiName?.StructuralText is { Length: > 0 } api && method is not null && url is not null
                    ? $"{api}|{method}|{url}"
                    : null,

            _ => null
        };
    }

    /// <summary>§4.5 determine_severity.</summary>
    internal static DuplicateSeverity DetermineSeverity(
        OperationMatchKey key,
        List<(ParsedApiGroup Group, ParsedApiOperation Operation)> members)
    {
        var sameApi = members
            .Select(m => m.Operation.ApiName?.StructuralText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1;

        if (key == OperationMatchKey.OperationId)
        {
            var sameGroup = members.Select(m => m.Group.ApiGroupName).Distinct().Count() == 1;
            if (sameGroup)
                return DuplicateSeverity.HardDuplicate;
        }

        if (key is OperationMatchKey.MethodAndUrl or OperationMatchKey.ApiAndMethodAndUrl)
            return sameApi ? DuplicateSeverity.LogicalDuplicate : DuplicateSeverity.CrossApiSimilarity;

        return DuplicateSeverity.CrossApiSimilarity;
    }
}
