namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Options for building a fresh APIM Terraform AST from an
/// <see cref="Models.ApimConfiguration"/>.
/// </summary>
public sealed record BuildOptions
{
    public ApimTemplateProfile Profile { get; init; } = ApimTemplateProfile.UserExampleProfile;

    /// <summary>Wrapper structure above the api group. Null/empty → flat root.</summary>
    public IReadOnlyList<string>? ApiGroupParentPath { get; init; }
        = ["apis", "bpc_apis", "backend_apis"];

    /// <summary>Add the 2–3 line leading comment block above each operation.</summary>
    public bool AddOperationComments { get; init; } = true;

    /// <summary>Add the REPLACE BEFORE APPLY header before api_operations.</summary>
    public bool AddReplaceBeforeApplyHeader { get; init; } = true;

    public OperationCommentSource CommentSource { get; init; } = OperationCommentSource.OpenApi;
}

/// <summary>Options for applying a template profile to an existing document.</summary>
public sealed record ApplyProfileOptions
{
    /// <summary>
    /// Apply the profile to fields that already have a value?
    /// false (default) — only to empty/missing ones.
    /// </summary>
    public bool OverwriteExisting { get; init; }

    /// <summary>Add REPLACE BEFORE APPLY comments before modified blocks.</summary>
    public bool AddReplaceComments { get; init; } = true;
}
