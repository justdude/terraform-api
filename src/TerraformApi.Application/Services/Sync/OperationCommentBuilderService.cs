using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Hcl;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Builds the leading comment block for inserted operations (§REV-1.5.4):
/// line 1 — strictly "METHOD URL_TEMPLATE | op_id: ID";
/// line 2 — display_name / source / inserted date;
/// line 3 — only when placeholders exist: "placeholders to replace: ...".
/// </summary>
public sealed class OperationCommentBuilderService : IOperationCommentBuilder
{
    /// <inheritdoc />
    public List<HclComment> Build(OperationCommentSpec spec)
    {
        var comments = new List<HclComment>
        {
            new()
            {
                Kind = HclCommentKind.LineHash,
                IsLeading = true,
                Text = $" {spec.Method.ToUpperInvariant()} {spec.UrlTemplate} | op_id: {spec.OperationId}"
            }
        };

        var displayPart = string.IsNullOrEmpty(spec.DisplayName)
            ? ""
            : $"display_name: \"{spec.DisplayName}\" · ";
        var sourcePart = $"source: {spec.Source} · ";
        var datePart = $"inserted: {spec.InsertedAt:yyyy-MM-dd} by sync";
        comments.Add(new HclComment
        {
            Kind = HclCommentKind.LineHash,
            IsLeading = true,
            Text = $" {displayPart}{sourcePart}{datePart}"
        });

        if (spec.PlaceholdersToReplace.Count > 0)
        {
            comments.Add(new HclComment
            {
                Kind = HclCommentKind.LineHash,
                IsLeading = true,
                Text = $" placeholders to replace: {string.Join(", ", spec.PlaceholdersToReplace.Select(p => $"${{{p}}}"))}"
            });
        }

        return comments;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractPlaceholders(HclObject operationNode)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        Collect(operationNode, names);
        return names.ToList();
    }

    private static void Collect(HclValue value, SortedSet<string> names)
    {
        switch (value)
        {
            case HclInterpolation interpolation:
                foreach (var expr in interpolation.ReferencedExpressions)
                    names.Add(expr);
                break;

            case HclObject obj:
                foreach (var assignment in obj.Assignments)
                    Collect(assignment.Value, names);
                break;

            case HclArray array:
                foreach (var item in array.Items)
                    Collect(item.Value, names);
                break;

            case HclHeredoc heredoc:
                foreach (var expr in HclParserServiceReferences(heredoc.Content))
                    names.Add(expr);
                break;
        }
    }

    private static IEnumerable<string> HclParserServiceReferences(string text) =>
        Hcl.HclParserService.ExtractReferences(text);
}
