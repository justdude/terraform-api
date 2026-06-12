namespace TerraformApi.Domain.Models.Sync;

/// <summary>
/// The result of analyzing an existing Terraform file's templating style.
/// </summary>
public sealed record DetectedProfile
{
    /// <summary>
    /// A profile built from the file's actual data. Can be used immediately
    /// to generate new operations in the same style.
    /// </summary>
    public required ApimTemplateProfile InferredProfile { get; init; }

    /// <summary>Which fields were encountered as interpolations (with frequencies).</summary>
    public List<DetectedField> DetectedFields { get; init; } = [];

    /// <summary>
    /// All ${...} names that appeared at least once — the "global dictionary" of
    /// variables the user must define in .tfvars.
    /// </summary>
    public HashSet<string> AllReferencedVariables { get; init; } = [];

    /// <summary>
    /// Literal values observed per field (e.g. "apim-company-dev" in apim_name).
    /// Useful for autocomplete suggestions when generating.
    /// </summary>
    public IReadOnlyDictionary<string, List<string>> LiteralValuesByField { get; init; }
        = new Dictionary<string, List<string>>();

    public StylingConfidence Confidence { get; init; }

    /// <summary>Which ready-made profile is closest to the detected one.</summary>
    public string? ClosestKnownProfileName { get; init; }
}

/// <summary>Per-field detection statistics.</summary>
public sealed record DetectedField
{
    /// <summary>"api.apim_name" or "api_operation.operation_id".</summary>
    public required string FieldPath { get; init; }

    public int TemplatedOccurrences { get; init; }
    public int LiteralOccurrences { get; init; }

    /// <summary>Observed interpolation expressions, e.g. ["${apim_name}"].</summary>
    public List<string> ObservedExpressions { get; init; } = [];

    /// <summary>Observed literal values, e.g. ["apim-company-dev"].</summary>
    public List<string> ObservedLiterals { get; init; } = [];
}

/// <summary>How heavily templated the analyzed file is.</summary>
public enum StylingConfidence
{
    /// <summary>&gt;70% of fields are interpolations.</summary>
    HighlyTemplated,

    /// <summary>30–70% — mixed.</summary>
    Mixed,

    /// <summary>&lt;30% — almost everything literal.</summary>
    MostlyLiteral,

    /// <summary>File is empty / has no operations.</summary>
    Empty
}
