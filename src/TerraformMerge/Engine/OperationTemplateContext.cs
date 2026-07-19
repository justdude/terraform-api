using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Hcl;

namespace TerraformMerge.Engine;

/// <summary>
/// Field values reused when generating a new operation block so it blends into
/// the target file's style (resource group, APIM instance, api name). Derived
/// from an existing operation/api block, falling back to placeholder tags.
/// </summary>
public sealed record OperationTemplateContext(
    string ResourceGroup,
    string ApimName,
    string ApiName)
{
    public static OperationTemplateContext Placeholders { get; } =
        new("{stage-group-name}", "{apim-name}", "{api-name}");

    /// <summary>Derives a context from the first Terraform-sourced operation in the list.</summary>
    public static OperationTemplateContext FromOriginal(LoadedOperations original)
    {
        var doc = original.TerraformDocument;
        if (doc is not null)
        {
            foreach (var group in doc.ApiGroups)
            {
                var context = FromGroup(group);
                if (context != Placeholders)
                    return context;
            }
        }

        return Placeholders;
    }

    /// <summary>
    /// Derives a context from one api group, preferring its first operation and
    /// falling back to its api block. Values are read as structural text, so an
    /// interpolated field such as <c>apim_name = "${var.apim_name}"</c> is
    /// carried through verbatim instead of degrading to a placeholder tag.
    /// </summary>
    public static OperationTemplateContext FromGroup(ParsedApiGroup group)
    {
        var op = group.Operations.FirstOrDefault();
        if (op is not null)
        {
            return new OperationTemplateContext(
                op.ApimResourceGroupName?.StructuralText ?? Placeholders.ResourceGroup,
                Text(op.AstNode, "apim_name") ?? Placeholders.ApimName,
                op.ApiName?.StructuralText ?? Placeholders.ApiName);
        }

        var api = group.Apis.FirstOrDefault();
        if (api is not null)
        {
            return new OperationTemplateContext(
                api.ApimResourceGroupName.StructuralText ?? Placeholders.ResourceGroup,
                api.ApimName.StructuralText ?? Placeholders.ApimName,
                api.Name.StructuralText ?? Placeholders.ApiName);
        }

        return Placeholders;
    }

    private static string? Text(HclObject node, string key) =>
        new HclValueRef { Node = node.Get(key) }.StructuralText;
}
