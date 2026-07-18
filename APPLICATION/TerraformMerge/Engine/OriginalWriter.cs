using TerraformApi.Application.Services.Hcl;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Rewrites the Original Terraform file so its <c>api_operations</c> array
/// matches a desired ordered list of operations. Operations that came from the
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

        var ctx = context ?? OperationTemplateContext.FromOriginal(original);

        var group = SelectTargetGroup(doc, desired)
            ?? throw new InvalidOperationException("No api group found in the original document.");

        if (group.AstNode.Get("api_operations") is not HclArray array)
        {
            array = new HclArray();
            group.AstNode.Items.Add(new HclAssignment { Key = "api_operations", Value = array });
        }

        var newItems = new List<HclArrayItem>();
        foreach (var node in desired)
        {
            if (node.Source == OperationSource.OriginalTerraform && node.ArrayItem is not null)
                newItems.Add(node.ArrayItem);                       // reuse — preserves formatting
            else
                newItems.Add(OperationHclBuilder.BuildArrayItem(node, ctx)); // generate canonically
        }

        // Only dirty the array when the operation set actually changed (by
        // reference and order); an unchanged rewrite then round-trips the file
        // byte-for-byte via the writer's whole-document fast path.
        var changed = !array.Items.SequenceEqual(newItems);

        array.Items.Clear();
        array.Items.AddRange(newItems);
        if (changed)
            array.Dirty = true; // force re-render so reorders/removals/additions take effect

        return _writer.Write(doc.Ast);
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
