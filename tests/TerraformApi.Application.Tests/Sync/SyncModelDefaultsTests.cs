using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Tests.Sync;

/// <summary>
/// Phase 2 acceptance: the append-only defaults of <see cref="MergePolicy"/>
/// and the three ready-made <see cref="ApimTemplateProfile"/>s are correct.
/// </summary>
public class SyncModelDefaultsTests
{
    [Fact]
    public void MergePolicy_DefaultFieldPolicies_IdentityFieldsArePreserve()
    {
        var policy = new MergePolicy();

        Assert.Equal(FieldMergePolicy.Preserve, policy.OperationFieldPolicies["operation_id"]);
        Assert.Equal(FieldMergePolicy.Preserve, policy.OperationFieldPolicies["method"]);
        Assert.Equal(FieldMergePolicy.Preserve, policy.OperationFieldPolicies["url_template"]);
    }

    [Fact]
    public void MergePolicy_DefaultFieldPolicies_DocFieldsAreEnrichIfMissing()
    {
        var policy = new MergePolicy();

        Assert.Equal(FieldMergePolicy.EnrichIfMissing, policy.OperationFieldPolicies["display_name"]);
        Assert.Equal(FieldMergePolicy.EnrichIfMissing, policy.OperationFieldPolicies["description"]);
        Assert.Equal(FieldMergePolicy.EnrichIfMissing, policy.OperationFieldPolicies["status_code"]);
    }

    [Fact]
    public void MergePolicy_Defaults_AppendOnlySemantics()
    {
        var policy = new MergePolicy();

        Assert.Equal(OperationPreservationMode.Preserve, policy.UnknownOperationPolicy);
        Assert.Equal(NewOperationMode.Append, policy.NewOperationPolicy);
    }

    [Fact]
    public void MergePolicy_DefaultCollectionPolicies_AppendMissing()
    {
        var policy = new MergePolicy();

        Assert.Equal(CollectionMergePolicy.AppendMissing, policy.CollectionPolicies["request.header"]);
        Assert.Equal(CollectionMergePolicy.AppendMissing, policy.CollectionPolicies["request.query"]);
        Assert.Equal(CollectionMergePolicy.AppendMissing, policy.CollectionPolicies["responses"]);
    }

    [Fact]
    public void MergePolicy_ApiFieldPolicies_AllPreserve()
    {
        var policy = new MergePolicy();
        Assert.All(policy.ApiFieldPolicies.Values, p => Assert.Equal(FieldMergePolicy.Preserve, p));
    }

    [Fact]
    public void MergePolicy_WithOverride_ChangesOnlyTargetField()
    {
        var policy = new MergePolicy().WithOverride("url_template", FieldMergePolicy.Overwrite);

        Assert.Equal(FieldMergePolicy.Overwrite, policy.OperationFieldPolicies["url_template"]);
        Assert.Equal(FieldMergePolicy.Preserve, policy.OperationFieldPolicies["operation_id"]);
    }

    [Fact]
    public void UserExampleProfile_HasExpectedTemplates()
    {
        var profile = ApimTemplateProfile.UserExampleProfile;

        Assert.Equal("${stage_group_name}", profile.ApiFieldTemplates["apim_resource_group_name"]);
        Assert.Equal("${api_name}-${env}", profile.ApiFieldTemplates["name"]);
        Assert.Equal("${operation_prefix}-${env}", profile.OperationFieldTemplates["operation_id"]);
        Assert.Equal("${operation_prefix}-${env}", profile.OperationIdTemplate);
        Assert.True(profile.KeepRoutingFieldsLiteral);
    }

    [Fact]
    public void ExtendedProfile_UsesOpSubstitutionAndExtraPlaceholders()
    {
        var profile = ApimTemplateProfile.ExtendedProfile;

        Assert.Contains("{op}", profile.OperationIdTemplate);
        Assert.Contains("${backend_url_protocol}", profile.ApiFieldTemplates["service_url"]);
        Assert.Equal("${subscription_required}", profile.ApiFieldTemplates["subscription_required"]);
    }

    [Fact]
    public void LiteralProfile_HasNoTemplates()
    {
        var profile = ApimTemplateProfile.LiteralProfile;

        Assert.Empty(profile.ApiFieldTemplates);
        Assert.Empty(profile.OperationFieldTemplates);
        Assert.Null(profile.OperationIdTemplate);
    }

    [Fact]
    public void GetByName_ResolvesKnownProfiles()
    {
        Assert.Same(ApimTemplateProfile.UserExampleProfile, ApimTemplateProfile.GetByName("UserExampleProfile"));
        Assert.Same(ApimTemplateProfile.ExtendedProfile, ApimTemplateProfile.GetByName("ExtendedProfile"));
        Assert.Same(ApimTemplateProfile.LiteralProfile, ApimTemplateProfile.GetByName("LiteralProfile"));
        Assert.Null(ApimTemplateProfile.GetByName("NoSuchProfile"));
    }

    [Fact]
    public void ApimApiGroupKey_Equality_UsesResolvedWhenAvailable()
    {
        var raw = new ApimApiGroupKey
        {
            ApimResourceGroupNameRaw = "${stage_group_name}",
            ApiNameRaw = "${api_name}-${env}",
            ApimResourceGroupNameResolved = "rg-apim-dev",
            ApiNameResolved = "bpc-dev"
        };
        var literal = new ApimApiGroupKey
        {
            ApimResourceGroupNameRaw = "rg-apim-dev",
            ApiNameRaw = "BPC-DEV"
        };

        Assert.True(raw.Equals(literal));
        Assert.Equal(raw.GetHashCode(), literal.GetHashCode());
    }

    [Fact]
    public void ApimApiGroupKey_Equality_StructuralWhenUnresolved()
    {
        var a = new ApimApiGroupKey { ApimResourceGroupNameRaw = "${rg}", ApiNameRaw = "${api}" };
        var b = new ApimApiGroupKey { ApimResourceGroupNameRaw = "${rg}", ApiNameRaw = "${api}" };
        var c = new ApimApiGroupKey { ApimResourceGroupNameRaw = "${other}", ApiNameRaw = "${api}" };

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }
}
