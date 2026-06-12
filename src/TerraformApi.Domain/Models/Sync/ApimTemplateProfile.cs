namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// Templating profile: which HCL fields map to which Terraform expressions.
/// Fields not present in the dictionaries are emitted as literals taken from
/// OpenAPI or the conversion settings.
/// </summary>
public sealed record ApimTemplateProfile
{
    public required string Name { get; init; }

    /// <summary>
    /// Mapping "api field name" → HCL value (as it appears inside the quotes,
    /// with ${...} interpolations).
    /// </summary>
    public IReadOnlyDictionary<string, string> ApiFieldTemplates { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Mapping "operation field name" → HCL value.</summary>
    public IReadOnlyDictionary<string, string> OperationFieldTemplates { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Template for operation_id supporting substitution: if it contains
    /// <c>{op}</c>, that is replaced with the kebab-cased operationId from
    /// OpenAPI. Default is a common prefix without substitution.
    /// </summary>
    public string? OperationIdTemplate { get; init; }

    public CorsTemplateVariables CorsVariables { get; init; } = new();

    /// <summary>
    /// Keep url_template and method as literals (recommended — they are the API contract).
    /// </summary>
    public bool KeepRoutingFieldsLiteral { get; init; } = true;

    /// <summary>Templatize display_name (default: keep the literal from OpenAPI summary).</summary>
    public bool TemplatizeDisplayName { get; init; }

    // ================ Ready-made profiles ================

    /// <summary>Profile 1 — exactly matches the user's working example.</summary>
    public static readonly ApimTemplateProfile UserExampleProfile = new()
    {
        Name = "UserExampleProfile",
        ApiFieldTemplates = new Dictionary<string, string>
        {
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"] = "${apim_name}",
            ["name"] = "${api_name}-${env}",
            ["display_name"] = "${api_display_name} - ${env}",
            ["path"] = "${api_path_prefix}.${env}/v1/${api_path_suffix}",
            ["service_url"] = "https://${api_gateway_host}/${api_version}/${backend_service_path}/",
            ["revision"] = "${api_revision}",
            ["product_id"] = "${product_id}"
        },
        OperationFieldTemplates = new Dictionary<string, string>
        {
            ["operation_id"] = "${operation_prefix}-${env}",
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"] = "${apim_name}",
            ["api_name"] = "${api_name}-${env}"
        },
        OperationIdTemplate = "${operation_prefix}-${env}"
    };

    /// <summary>Profile 2 — extended, with all recommended placeholders.</summary>
    public static readonly ApimTemplateProfile ExtendedProfile = new()
    {
        Name = "ExtendedProfile",
        ApiFieldTemplates = new Dictionary<string, string>
        {
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"] = "${apim_name}",
            ["name"] = "${api_name}-${api_version}-${env}",
            ["display_name"] = "${api_display_name} - ${env}",
            ["path"] = "${api_path_prefix}.${env}/${api_version}/${api_path_suffix}",
            ["service_url"] = "${backend_url_protocol}://${api_gateway_host}/${api_version}/${backend_service_path}/",
            ["revision"] = "${api_revision}",
            ["product_id"] = "${product_id}",
            ["subscription_required"] = "${subscription_required}"
        },
        OperationFieldTemplates = new Dictionary<string, string>
        {
            ["operation_id"] = "${operation_prefix}-{op}-${env}",
            ["apim_resource_group_name"] = "${stage_group_name}",
            ["apim_name"] = "${apim_name}",
            ["api_name"] = "${api_name}-${api_version}-${env}"
        },
        OperationIdTemplate = "${operation_prefix}-{op}-${env}"
    };

    /// <summary>Profile 3 — no templates, everything literal (for one-time generation).</summary>
    public static readonly ApimTemplateProfile LiteralProfile = new()
    {
        Name = "LiteralProfile",
        ApiFieldTemplates = new Dictionary<string, string>(),
        OperationFieldTemplates = new Dictionary<string, string>(),
        OperationIdTemplate = null,
        KeepRoutingFieldsLiteral = true
    };

    /// <summary>Resolves a known profile by name, or null for unknown names.</summary>
    public static ApimTemplateProfile? GetByName(string? name) => name switch
    {
        "UserExampleProfile" => UserExampleProfile,
        "ExtendedProfile" => ExtendedProfile,
        "LiteralProfile" => LiteralProfile,
        _ => null
    };
}

/// <summary>Template expressions used when generating CORS policy blocks.</summary>
public sealed record CorsTemplateVariables
{
    public string FrontendHostExpr { get; init; } = "${frontend_host}";
    public string EnvExpr { get; init; } = "${env}";
    public string CompanyDomainExpr { get; init; } = "${company_domain}";
    public string LocalDevHostExpr { get; init; } = "${local_dev_host}";
    public string LocalDevPortExpr { get; init; } = "${local_dev_port}";
    public string AllowCredentialsExpr { get; init; } = "true";
}
