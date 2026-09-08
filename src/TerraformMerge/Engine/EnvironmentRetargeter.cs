namespace TerraformMerge.Engine;

/// <summary>
/// What moving one operation to another environment would change. Produced
/// without touching the operation, so the editor can preview it and the list
/// panes can apply it.
/// </summary>
/// <param name="FromEnvironment">The environment the operation is in now, or null when it carries none.</param>
/// <param name="ToEnvironment">The environment it is being moved to.</param>
/// <param name="Fields">The editable fields whose value would change.</param>
/// <param name="OperationId">The operation id after the move (unchanged when it carries no environment token).</param>
/// <param name="RenamesOperationId">True when <paramref name="OperationId"/> differs from the operation's current id.</param>
public sealed record EnvironmentRetargetPlan(
    string? FromEnvironment,
    string ToEnvironment,
    IReadOnlyDictionary<OperationField, string> Fields,
    string OperationId,
    bool RenamesOperationId)
{
    public bool HasChanges => Fields.Count > 0 || RenamesOperationId;
}

/// <summary>
/// Moves an operation from one environment to another — the "add the operations
/// missing from qa out of the dev config, as qa operations" step.
///
/// Two sources feed the new values, in order:
/// <list type="number">
/// <item>the <b>destination environment's own values</b> from the loaded
/// documents (<see cref="EnvironmentCatalog"/>) — so a dev operation moved to qa
/// gets the qa file's actual resource group / APIM / api name;</item>
/// <item>failing that (the destination is not in either file, or its operations
/// disagree), the operation's own value with its <b>environment token
/// rewritten</b> — <c>rg-apim-dev</c> → <c>rg-apim-qa</c>.</item>
/// </list>
///
/// Interpolated values (<c>${var.resource_group}</c>) are left alone: they are
/// already environment-neutral, and hard-coding one would undo what the file
/// deliberately left variable. Method, URL template and status code are never
/// touched — they are the operation's route, not its environment.
///
/// <c>operation_id</c> is the exception to "identity is never merged": it is not
/// merged <i>from the other side</i> here, its own environment suffix is
/// rewritten (<c>list-orders-dev</c> → <c>list-orders-qa</c>), because the id
/// must be unique per APIM instance and a dev id in the qa config is a bug.
/// </summary>
public static class EnvironmentRetargeter
{
    /// <summary>Per-operation text that carries the environment name but not its identity.</summary>
    private static readonly OperationField[] TextFields =
        [OperationField.DisplayName, OperationField.Description];

    private static readonly IReadOnlyDictionary<OperationField, string> NoFields =
        new Dictionary<OperationField, string>();

    /// <summary>
    /// Works out what moving <paramref name="node"/> to
    /// <paramref name="targetEnvironment"/> would change, without changing
    /// anything. A blank target, or one the operation is already in, plans nothing.
    /// </summary>
    public static EnvironmentRetargetPlan Plan(
        OperationNode node,
        string? targetEnvironment,
        EnvironmentCatalog? catalog = null)
    {
        var to = (targetEnvironment ?? "").Trim();
        var from = EnvironmentCatalog.Detect(node);

        if (to.Length == 0 || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return new EnvironmentRetargetPlan(from, to, NoFields, node.OperationId, false);

        var profile = catalog?.Profile(to);
        var fields = new Dictionary<OperationField, string>();

        foreach (var field in EnvironmentCatalog.ProfileFields)
            Propose(node, field, from, to, profile?.Value(field), fields);

        foreach (var field in TextFields)
            Propose(node, field, from, to, null, fields);

        var operationId = Rewrite(node.OperationId, from, to);
        var renames = !string.Equals(operationId, node.OperationId, StringComparison.Ordinal);

        return new EnvironmentRetargetPlan(from, to, fields, operationId, renames);
    }

    /// <summary>Applies a plan, reporting whether anything actually changed.</summary>
    public static bool Apply(OperationNode node, EnvironmentRetargetPlan plan)
    {
        var changed = plan.Fields.Count > 0 && OperationEditor.Apply(node, plan.Fields);

        if (plan.RenamesOperationId)
            changed |= OperationEditor.ApplyOperationId(node, plan.OperationId);

        return changed;
    }

    /// <summary>Plans and applies in one step. Returns true when the operation changed.</summary>
    public static bool Retarget(
        OperationNode node,
        string? targetEnvironment,
        EnvironmentCatalog? catalog = null) =>
        Apply(node, Plan(node, targetEnvironment, catalog));

    /// <summary>
    /// Adds the new value for one field to <paramref name="fields"/> when it
    /// differs: the destination environment's known value if there is one,
    /// otherwise the current value with its environment token rewritten.
    /// </summary>
    private static void Propose(
        OperationNode node,
        OperationField field,
        string? from,
        string to,
        string? known,
        Dictionary<OperationField, string> fields)
    {
        var info = OperationFieldInfo.For(field);
        var current = info.Get(node);

        if (IsInterpolated(current))
            return;

        var proposed = !string.IsNullOrEmpty(known) ? known : Rewrite(current, from, to);

        if (!string.Equals(proposed, current, StringComparison.Ordinal))
            fields[field] = proposed;
    }

    /// <summary>The value with its <paramref name="from"/> segment replaced; unchanged when there is nothing to rewrite.</summary>
    private static string Rewrite(string value, string? from, string to) =>
        from is null || IsInterpolated(value) ? value : EnvironmentToken.Replace(value, from, to);

    private static bool IsInterpolated(string value) =>
        value.Contains("${", StringComparison.Ordinal);
}
