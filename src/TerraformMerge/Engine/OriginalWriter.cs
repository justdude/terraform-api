using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Rewrites the Original Terraform file so its <c>api_operations</c> arrays
/// match a desired ordered list of operations. Operations that came from the
/// Original document are reused verbatim (byte-for-byte via the format-preserving
/// writer); operations from other sources are generated as canonical blocks.
/// </summary>
public sealed class OriginalWriter
{
    private readonly IHclWriter _writer = new HclWriterService();

    /// <summary>Produces the rewritten Terraform text. Does not touch disk.</summary>
    public string Rewrite(
        LoadedOperations original,
        IReadOnlyList<OperationNode> desired,
        OperationTemplateContext? context = null)
    {
        var doc = original.TerraformDocument
            ?? throw new InvalidOperationException("Original must be a Terraform document to rewrite.");

        var fallback = SelectTargetGroup(doc, desired)
            ?? throw new InvalidOperationException("No api group found in the original document.");

        // A document may hold several api groups (backend_apis is a map). Each
        // group owns its own api_operations array, so the desired list is
        // partitioned per group and every group is rewritten independently —
        // writing them all into one group would duplicate the other groups'
        // operations and leave the originals behind.
        var groups = doc.ApiGroups
            .Where(g => g.AstNode.Get("api_operations") is HclArray)
            .ToList();
        if (!groups.Any(g => ReferenceEquals(g, fallback)))
            groups.Add(fallback);

        var buckets = groups.Select(_ => new List<OperationNode>()).ToList();

        foreach (var node in desired)
        {
            // Operations read from this document return to their own group;
            // anything else (a new operation, or one from another file) lands in
            // the fallback group.
            var index = node.Source == OperationSource.OriginalTerraform && node.ApiGroupName is not null
                ? groups.FindIndex(g => g.ApiGroupName == node.ApiGroupName)
                : -1;
            if (index < 0)
                index = groups.FindIndex(g => ReferenceEquals(g, fallback));

            buckets[index].Add(node);
        }

        for (var i = 0; i < groups.Count; i++)
            WriteGroup(groups[i], buckets[i], context);

        return _writer.Write(doc.Ast);
    }

    /// <summary>
    /// Replaces one group's <c>api_operations</c> array with the given nodes.
    /// The template context is derived from the group being written so a
    /// generated block blends into <i>that</i> group's style, not another's.
    /// </summary>
    private static void WriteGroup(
        ParsedApiGroup group, List<OperationNode> nodes, OperationTemplateContext? context)
    {
        if (group.AstNode.Get("api_operations") is not HclArray array)
        {
            if (nodes.Count == 0)
                return;

            array = new HclArray();
            group.AstNode.Items.Add(new HclAssignment { Key = "api_operations", Value = array });
        }

        var ctx = context ?? OperationTemplateContext.FromGroup(group);

        var newItems = new List<HclArrayItem>();
        foreach (var node in nodes)
        {
            if (node.Source == OperationSource.OriginalTerraform && node.ArrayItem is not null)
                newItems.Add(node.ArrayItem);                       // reuse — preserves formatting
            else
                newItems.Add(OperationHclBuilder.BuildArrayItem(node, ctx)); // generate canonically
        }

        // Only dirty the array when the operation set actually changed (by
        // reference and order); an unchanged rewrite then round-trips the file
        // byte-for-byte via the writer's whole-document fast path.
        var changed = array.Items.Count != newItems.Count
            || !array.Items.Zip(newItems, ReferenceEquals).All(same => same);

        array.Items.Clear();
        array.Items.AddRange(newItems);
        if (changed)
            array.Dirty = true; // force re-render so reorders/removals/additions take effect
    }

    private static ParsedApiGroup? SelectTargetGroup(ParsedApimDocument doc, IReadOnlyList<OperationNode> desired)
    {
        // Prefer the group the retained original operations belong to.
        var preferredName = desired
            .Where(n => n.Source == OperationSource.OriginalTerraform && n.ApiGroupName is not null)
            .GroupBy(n => n.ApiGroupName!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        if (preferredName is not null)
        {
            var match = doc.ApiGroups.FirstOrDefault(g => g.ApiGroupName == preferredName);
            if (match is not null)
                return match;
        }

        return doc.ApiGroups.FirstOrDefault(g => g.AstNode.Get("api_operations") is HclArray)
               ?? doc.ApiGroups.FirstOrDefault();
    }
}
