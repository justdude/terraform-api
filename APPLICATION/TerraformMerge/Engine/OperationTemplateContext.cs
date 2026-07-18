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
                var op = group.Operations.FirstOrDefault();
                if (op is not null)
                {
                    return new OperationTemplateContext(
                        op.ApimResourceGroupName?.StructuralText ?? Placeholders.ResourceGroup,
                        op.AstNode.Get("apim_name") is HclLiteral n ? n.RawValue : Placeholders.ApimName,
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
            }
        }

        return Placeholders;
    }
}
