using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;
using TerraformApi.Domain.Models.Apim;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Builds and matches operation fingerprints (§4.4). Matching walks the
/// strategy's keys in order; the first key that yields exactly one candidate
/// wins. When structural matching leaves unmatched operations and a variable
/// context is available, fingerprints are re-built in resolved mode and the
/// match loop runs again.
/// </summary>
public sealed partial class OperationMatcherService : IOperationMatcher
{
    private readonly TerraformInterpolationResolver _resolver;

    public OperationMatcherService(TerraformInterpolationResolver resolver) => _resolver = resolver;

    [GeneratedRegex(@"\{([^}/]+)\}")]
    private static partial Regex BraceParamRegex();

    [GeneratedRegex(@":([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ColonParamRegex();

    // -----------------------------------------------------------------
    // Fingerprinting
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public OperationFingerprint FingerprintFromOpenApi(
        ApimApiOperation operation, OperationMatchStrategy strategy) => new()
    {
        OperationId = operation.OperationId,
        Method = operation.Method.ToUpperInvariant(),
        UrlTemplate = NormalizeUrl(operation.UrlTemplate, strategy.UrlNormalization),
        ParameterSignature = BuildParameterSignature(operation, strategy),
        ApiName = operation.ApiName,
        ApiResourceGroup = operation.ApimResourceGroupName,
        SourceMarker = OperationFingerprintSource.OpenApi
    };

    /// <inheritdoc />
    public OperationFingerprint FingerprintFromTerraform(
        ParsedApiOperation operation, OperationMatchStrategy strategy) => new()
    {
        OperationId = operation.OperationId.StructuralText,
        Method = operation.Method.StructuralText?.ToUpperInvariant(),
        UrlTemplate = operation.UrlTemplate.StructuralText is { } url
            ? NormalizeUrl(url, strategy.UrlNormalization)
            : null,
        ParameterSignature = BuildParameterSignature(operation, strategy),
        ApiName = operation.ApiName?.StructuralText,
        ApiResourceGroup = operation.ApimResourceGroupName?.StructuralText,
        SourceMarker = OperationFingerprintSource.ExistingTerraform
    };

    /// <summary>Normalizes a URL template per the strategy options.</summary>
    internal static string NormalizeUrl(string url, UrlNormalizationOptions options)
    {
        var result = url;

        if (options.LowercaseScheme)
        {
            var schemeEnd = result.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd > 0)
                result = result[..schemeEnd].ToLowerInvariant() + result[schemeEnd..];
        }

        if (options.NormalizeBraceParams)
        {
            // Unify :param syntax into {param}; parameter names are kept.
            result = ColonParamRegex().Replace(result, "{$1}");
        }

        if (options.CollapseSlashes)
        {
            // Collapse duplicate slashes in the path only — never inside the
            // scheme separator or the query/fragment portion.
            var queryStart = result.IndexOfAny(['?', '#']);
            var path = queryStart >= 0 ? result[..queryStart] : result;
            var suffix = queryStart >= 0 ? result[queryStart..] : "";

            var schemeEnd = path.IndexOf("://", StringComparison.Ordinal);
            var prefix = schemeEnd > 0 ? path[..(schemeEnd + 3)] : "";
            var rest = schemeEnd > 0 ? path[(schemeEnd + 3)..] : path;
            while (rest.Contains("//"))
                rest = rest.Replace("//", "/");
            result = prefix + rest + suffix;
        }

        if (options.TrimTrailingSlash && result.Length > 1)
            result = result.TrimEnd('/');

        if (options.TreatLeadingSlashAsOptional)
            result = result.TrimStart('/');

        if (options.IgnoreCase)
            result = result.ToLowerInvariant();

        return result;
    }

    /// <summary>h:/q:/t: parts, sorted, case-insensitive names (§4.4).</summary>
    internal static string BuildParameterSignature(ApimApiOperation operation, OperationMatchStrategy strategy)
    {
        var parts = new List<string>();

        foreach (var request in operation.Requests)
        {
            foreach (var header in request.Headers)
                parts.Add(SignaturePart("h", header.Name, header.Type, strategy));
            foreach (var query in request.QueryParameters)
                parts.Add(SignaturePart("q", query.Name, query.Type, strategy));
        }

        foreach (var name in TemplateParameterNames(operation.UrlTemplate))
            parts.Add(SignaturePart("t", name, "string", strategy));

        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    /// <summary>Terraform side: reads request[].header[]/query_parameter[] from the AST.</summary>
    internal static string BuildParameterSignature(ParsedApiOperation operation, OperationMatchStrategy strategy)
    {
        var parts = new List<string>();

        if (operation.RequestArray is not null)
        {
            foreach (var requestItem in operation.RequestArray.Items)
            {
                if (requestItem.Value is not Domain.Models.Hcl.HclObject requestObject)
                    continue;

                CollectParameterNames(requestObject, "header", "h", parts, strategy);
                CollectParameterNames(requestObject, "query_parameter", "q", parts, strategy);
            }
        }

        if (operation.UrlTemplate.StructuralText is { } url)
        {
            foreach (var name in TemplateParameterNames(url))
                parts.Add(SignaturePart("t", name, "string", strategy));
        }

        parts.Sort(StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    private static void CollectParameterNames(
        Domain.Models.Hcl.HclObject requestObject,
        string arrayKey,
        string prefix,
        List<string> parts,
        OperationMatchStrategy strategy)
    {
        if (requestObject.Get(arrayKey) is not Domain.Models.Hcl.HclArray array)
            return;

        foreach (var item in array.Items)
        {
            if (item.Value is not Domain.Models.Hcl.HclObject paramObject)
                continue;

            var name = (paramObject.Get("name") as Domain.Models.Hcl.HclLiteral)?.RawValue;
            if (name is null)
                continue;

            var type = (paramObject.Get("type") as Domain.Models.Hcl.HclLiteral)?.RawValue ?? "string";
            parts.Add(SignaturePart(prefix, name, type, strategy));
        }
    }

    private static string SignaturePart(string prefix, string name, string type, OperationMatchStrategy strategy) =>
        strategy.IncludeParameterTypesInSignature
            ? $"{prefix}:{name.ToLowerInvariant()}:{type}"
            : $"{prefix}:{name.ToLowerInvariant()}";

    private static IEnumerable<string> TemplateParameterNames(string urlTemplate) =>
        BraceParamRegex().Matches(urlTemplate).Select(m => m.Groups[1].Value);

    // -----------------------------------------------------------------
    // Matching
    // -----------------------------------------------------------------

    /// <inheritdoc />
    public MatchResult Match(
        IReadOnlyList<OperationFingerprint> openApiFingerprints,
        IReadOnlyList<OperationFingerprint> terraformFingerprints,
        OperationMatchStrategy strategy,
        ApimApiGroupKey? scopeKey = null)
    {
        var remainingOpenApi = openApiFingerprints.ToList();
        var remainingTf = terraformFingerprints
            .Where(tf => InScope(tf, scopeKey, strategy))
            .ToList();

        var matched = new List<(OperationFingerprint Tf, OperationFingerprint OpenApi)>();
        var ambiguities = new List<AmbiguousMatch>();

        RunMatchLoop(remainingOpenApi, remainingTf, strategy, matched, ambiguities, identity: f => f);

        // Resolved-mode fallback: re-run with variables substituted.
        if (strategy.TryResolvedComparisonAsFallback
            && strategy.VariableContext is { Count: > 0 }
            && remainingOpenApi.Count > 0
            && remainingTf.Count > 0)
        {
            RunMatchLoop(remainingOpenApi, remainingTf, strategy, matched, ambiguities,
                identity: f => ResolveFingerprint(f, strategy));
        }

        // Fingerprints that hit an ambiguity are excluded from "only in" partitions:
        // acting on them automatically would risk duplicating an existing operation.
        var ambiguousSources = ambiguities.Select(a => a.Source).ToHashSet();

        return new MatchResult
        {
            Matched = matched,
            OnlyInOpenApi = remainingOpenApi.Where(f => !ambiguousSources.Contains(f)).ToList(),
            OnlyInTerraform = remainingTf,
            Ambiguities = ambiguities
        };
    }

    private static void RunMatchLoop(
        List<OperationFingerprint> remainingOpenApi,
        List<OperationFingerprint> remainingTf,
        OperationMatchStrategy strategy,
        List<(OperationFingerprint Tf, OperationFingerprint OpenApi)> matched,
        List<AmbiguousMatch> ambiguities,
        Func<OperationFingerprint, OperationFingerprint> identity)
    {
        foreach (var key in strategy.Keys)
        {
            if (remainingOpenApi.Count == 0)
                break;

            // Reverse index of remaining TF fingerprints on this key.
            var tfIndex = new Dictionary<string, List<OperationFingerprint>>(StringComparer.Ordinal);
            foreach (var tf in remainingTf)
            {
                var value = KeyValue(identity(tf), key, strategy);
                if (value is null)
                    continue;
                if (!tfIndex.TryGetValue(value, out var list))
                    tfIndex[value] = list = [];
                list.Add(tf);
            }

            foreach (var openApiFp in remainingOpenApi.ToList())
            {
                var value = KeyValue(identity(openApiFp), key, strategy);
                if (value is null || !tfIndex.TryGetValue(value, out var candidates) || candidates.Count == 0)
                    continue;

                if (candidates.Count == 1)
                {
                    var tf = candidates[0];
                    matched.Add((tf, openApiFp));
                    remainingTf.Remove(tf);
                    remainingOpenApi.Remove(openApiFp);
                    candidates.Clear();
                }
                else
                {
                    if (!ambiguities.Any(a => a.Source == openApiFp && a.AmbiguousOnKey == key))
                    {
                        ambiguities.Add(new AmbiguousMatch
                        {
                            Source = openApiFp,
                            Candidates = candidates.ToList(),
                            AmbiguousOnKey = key
                        });
                    }
                }
            }
        }
    }

    /// <summary>Composite key value for a fingerprint; null when a component is missing.</summary>
    internal static string? KeyValue(
        OperationFingerprint fingerprint,
        OperationMatchKey key,
        OperationMatchStrategy strategy)
    {
        return key switch
        {
            OperationMatchKey.OperationId =>
                NullIfEmpty(fingerprint.OperationId),

            OperationMatchKey.MethodAndUrl =>
                Combine(fingerprint.Method, fingerprint.UrlTemplate),

            OperationMatchKey.MethodAndUrlAndParams =>
                Combine(fingerprint.Method, fingerprint.UrlTemplate, fingerprint.ParameterSignature ?? ""),

            OperationMatchKey.Tag =>
                NullIfEmpty(fingerprint.Tag),

            OperationMatchKey.ApiAndMethodAndUrl =>
                Combine(fingerprint.ApiName, fingerprint.Method, fingerprint.UrlTemplate),

            OperationMatchKey.RgApiAndMethodAndUrl =>
                Combine(fingerprint.ApiResourceGroup, fingerprint.ApiName, fingerprint.Method, fingerprint.UrlTemplate),

            OperationMatchKey.Custom => null, // handled by CustomMatcher below

            _ => null
        };

        static string? NullIfEmpty(string? value) =>
            string.IsNullOrEmpty(value) ? null : value;

        static string? Combine(params string?[] parts) =>
            parts.Any(string.IsNullOrEmpty) ? null : string.Join("|", parts);
    }

    private OperationFingerprint ResolveFingerprint(
        OperationFingerprint fingerprint, OperationMatchStrategy strategy)
    {
        var variables = strategy.VariableContext!;
        return fingerprint with
        {
            OperationId = fingerprint.OperationId is null ? null : _resolver.Resolve(fingerprint.OperationId, variables),
            UrlTemplate = fingerprint.UrlTemplate is null
                ? null
                : NormalizeUrl(_resolver.Resolve(fingerprint.UrlTemplate, variables), strategy.UrlNormalization),
            ApiName = fingerprint.ApiName is null ? null : _resolver.Resolve(fingerprint.ApiName, variables),
            ApiResourceGroup = fingerprint.ApiResourceGroup is null ? null : _resolver.Resolve(fingerprint.ApiResourceGroup, variables),
            SourceMarker = OperationFingerprintSource.Resolved
        };
    }

    private bool InScope(
        OperationFingerprint fingerprint, ApimApiGroupKey? scopeKey, OperationMatchStrategy strategy)
    {
        if (scopeKey is null)
            return true;
        if (fingerprint.ApiResourceGroup is null || fingerprint.ApiName is null)
            return true; // can't scope without the fields — keep

        var candidate = new ApimApiGroupKey
        {
            ApimResourceGroupNameRaw = fingerprint.ApiResourceGroup,
            ApiNameRaw = fingerprint.ApiName,
            ApimResourceGroupNameResolved = strategy.VariableContext is { } vars
                ? _resolver.Resolve(fingerprint.ApiResourceGroup, vars)
                : null,
            ApiNameResolved = strategy.VariableContext is { } vars2
                ? _resolver.Resolve(fingerprint.ApiName, vars2)
                : null
        };

        return candidate.Equals(scopeKey);
    }
}
