using Microsoft.OpenApi.Models;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Resolves a stable type reference (schema_id / type_name) from the complex
/// schema shapes that appear in real-world OpenAPI documents. Policy
/// (documented in docs/openapi-complex-types.md):
///
///   RECOGNIZED (well-defined, single nameable type):
///   - direct $ref                          → Order   (even when the referenced
///     component is itself an array — the component name IS the type)
///   - allOf whose branches resolve to one  → Order   (Swashbuckle's nullable
///     reference pattern allOf:[{$ref}]+nullable, and single-base inheritance)
///   - array whose items resolve            → Order + "[]" suffix on type_name
///   - oneOf/anyOf where EVERY branch        → that type
///     resolves to the SAME type
///
///   IGNORED BY DESIGN (no single nameable type exists):
///   - oneOf/anyOf where branches differ or  → ambiguous union
///     any branch is anonymous/inline
///   - allOf merging several distinct $refs  → merged composite, unnamed
///   - inline object/primitive schemas       → anonymous
///
/// Array-ness is tracked THROUGH the recursion (not re-derived from the
/// top-level node), so a wrapped array (array inside a single-branch
/// oneOf/anyOf/allOf) keeps its "[]" suffix, and a bare $ref to an array
/// component does not gain a spurious one.
///
/// Ignoring never fails the conversion — the representation keeps its
/// content type and simply carries no schema_id/type_name.
/// </summary>
internal static class OpenApiSchemaInterpreter
{
    /// <summary>A resolved reference: the component id plus whether it denotes an inline array.</summary>
    private readonly record struct SchemaRef(string Id, bool IsInlineArray);

    /// <summary>
    /// Returns the resolved (SchemaId, TypeName) pair, or (null, null) when the
    /// schema has no single nameable type.
    /// </summary>
    public static (string? SchemaId, string? TypeName) ResolveTypeReference(OpenApiSchema? schema)
    {
        var resolved = Resolve(schema, depth: 0);
        if (resolved is not { } r)
            return (null, null);

        return (r.Id, r.IsInlineArray ? $"{r.Id}[]" : r.Id);
    }

    private static SchemaRef? Resolve(OpenApiSchema? schema, int depth)
    {
        // Defensive bound: real documents never nest wrapper schemas deeply,
        // and the 1.6 model can contain reference cycles.
        if (schema is null || depth > 4)
            return null;

        // 1. Direct $ref — the component name denotes the type verbatim, even
        //    when the reader also surfaces the referenced component's array
        //    Type/Items on the same node. No "[]" suffix here.
        if (schema.Reference?.Id is { Length: > 0 } directId)
            return new SchemaRef(directId, IsInlineArray: false);

        // 2. Inline array → resolve the element type and mark it as an array.
        if (schema.Type == "array" || schema.Items is not null)
        {
            var inner = Resolve(schema.Items, depth + 1);
            return inner is { } i ? new SchemaRef(i.Id, IsInlineArray: true) : null;
        }

        // 3. allOf: lenient merge — a single distinct named branch wins even
        //    alongside anonymous inline merge-parts (inheritance / nullable ref).
        var allOf = ResolveComposition(schema.AllOf, depth, requireEveryBranch: false);
        if (allOf is not null)
            return allOf;

        // 4. oneOf / anyOf: strict union — recognized only when EVERY branch
        //    resolves to the SAME type; any differing or anonymous branch makes
        //    it an ambiguous union that is ignored.
        return ResolveComposition(schema.OneOf, depth, requireEveryBranch: true)
            ?? ResolveComposition(schema.AnyOf, depth, requireEveryBranch: true);
    }

    /// <summary>
    /// Resolves a composition list to a single reference when unambiguous.
    /// <paramref name="requireEveryBranch"/> = true (oneOf/anyOf): every branch
    /// must resolve to the same id. false (allOf): anonymous branches are
    /// tolerated as long as exactly one distinct named id remains.
    /// </summary>
    private static SchemaRef? ResolveComposition(IList<OpenApiSchema>? branches, int depth, bool requireEveryBranch)
    {
        if (branches is not { Count: > 0 })
            return null;

        var resolved = branches.Select(b => Resolve(b, depth + 1)).ToList();

        if (requireEveryBranch && resolved.Any(r => r is null))
            return null;

        var present = resolved.Where(r => r is not null).Select(r => r!.Value).ToList();
        var distinctIds = present.Select(r => r.Id).Distinct().ToList();
        if (distinctIds.Count != 1)
            return null;

        return new SchemaRef(distinctIds[0], present.Any(r => r.IsInlineArray));
    }
}
