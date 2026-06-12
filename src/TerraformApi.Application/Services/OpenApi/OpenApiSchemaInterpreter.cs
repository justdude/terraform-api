using Microsoft.OpenApi.Models;

namespace TerraformApi.Application.Services.OpenApi;

/// <summary>
/// Resolves a stable type reference (schema_id / type_name) from the complex
/// schema shapes that appear in real-world OpenAPI documents. Policy
/// (documented in docs/openapi-complex-types.md):
///
///   RECOGNIZED (well-defined, single nameable type):
///   - direct $ref                          → Order
///   - allOf with exactly one $ref branch   → Order   (Swashbuckle's
///     "nullable reference" pattern: allOf:[{$ref}] + nullable:true)
///   - array whose items resolve            → Order / "Order[]"
///   - oneOf/anyOf with exactly ONE branch  → that branch's type
///
///   IGNORED BY DESIGN (no single nameable type exists):
///   - oneOf/anyOf with multiple branches   → ambiguous union
///   - allOf with multiple $ref branches    → merged composite, unnamed
///   - inline object/primitive schemas      → anonymous
///
/// Ignoring never fails the conversion — the representation keeps its
/// content type and simply carries no schema_id/type_name.
/// </summary>
internal static class OpenApiSchemaInterpreter
{
    /// <summary>
    /// Returns the resolved (SchemaId, TypeName) pair, or (null, null) when the
    /// schema has no single nameable type.
    /// </summary>
    public static (string? SchemaId, string? TypeName) ResolveTypeReference(OpenApiSchema? schema)
    {
        var id = ResolveReferenceId(schema, depth: 0);
        if (id is null)
            return (null, null);

        // Arrays advertise their element type with an [] suffix on type_name.
        if (schema?.Type == "array" || (schema?.Reference is null && schema?.Items is not null && schema.Type is null))
        {
            return (id, $"{id}[]");
        }

        return (id, id);
    }

    private static string? ResolveReferenceId(OpenApiSchema? schema, int depth)
    {
        // Defensive bound: real documents never nest wrapper schemas deeply,
        // and the 1.6 model can contain reference cycles.
        if (schema is null || depth > 4)
            return null;

        // 1. Direct $ref.
        if (schema.Reference?.Id is { Length: > 0 } directId)
            return directId;

        // 2. Array of T → element's reference.
        if (schema.Items is not null)
            return ResolveReferenceId(schema.Items, depth + 1);

        // 3. allOf: recognize only the single-$ref wrapper pattern
        //    (e.g. Swashbuckle nullable refs). Multiple branches form an
        //    unnamed composite — ignored by design.
        var allOfRefs = ResolveBranchIds(schema.AllOf, depth);
        if (allOfRefs is not null)
            return allOfRefs;

        // 4. oneOf / anyOf: only an effectively-single branch is unambiguous.
        return ResolveBranchIds(schema.OneOf, depth) ?? ResolveBranchIds(schema.AnyOf, depth);
    }

    /// <summary>
    /// Resolves a composition list to a reference id only when exactly one
    /// distinct id emerges — anything else is ambiguous and ignored.
    /// </summary>
    private static string? ResolveBranchIds(IList<OpenApiSchema>? branches, int depth)
    {
        if (branches is not { Count: > 0 })
            return null;

        var ids = branches
            .Select(b => ResolveReferenceId(b, depth + 1))
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        return ids.Count == 1 ? ids[0] : null;
    }
}
