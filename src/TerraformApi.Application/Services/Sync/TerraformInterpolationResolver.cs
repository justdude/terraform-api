using System.Text.RegularExpressions;
using TerraformApi.Domain.Models.Sync;

namespace TerraformApi.Application.Services.Sync;

/// <summary>
/// Substitutes variable values into Terraform interpolation templates.
/// Only simple <c>${var_name}</c> and <c>${var.path}</c> forms are supported;
/// complex expressions (functions, ternaries) are left as-is and reported
/// in <see cref="ResolveResult.UnresolvedExpressions"/>.
/// </summary>
public sealed partial class TerraformInterpolationResolver
{
    [GeneratedRegex(@"\$\{([^{}]*)\}")]
    private static partial Regex SimpleInterpolationRegex();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.-]*$")]
    private static partial Regex SimpleExpressionRegex();

    /// <summary>Resolves a template, leaving unknown/complex expressions untouched.</summary>
    public string Resolve(string template, IReadOnlyDictionary<string, string> variables) =>
        ResolveWithReport(template, variables).Value;

    /// <summary>Resolves a template and reports which expressions stayed unresolved.</summary>
    public ResolveResult ResolveWithReport(string template, IReadOnlyDictionary<string, string> variables)
    {
        var unresolved = new List<string>();

        var value = SimpleInterpolationRegex().Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim();

            // Only simple variable references are resolvable; anything with
            // operators, quotes or parens is a complex expression.
            if (SimpleExpressionRegex().IsMatch(expression) &&
                variables.TryGetValue(expression, out var replacement))
            {
                return replacement;
            }

            unresolved.Add(expression);
            return match.Value;
        });

        return new ResolveResult { Value = value, UnresolvedExpressions = unresolved };
    }
}
