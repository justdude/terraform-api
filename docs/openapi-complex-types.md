# OpenAPI complex types — recognition policy

This document explains how the converter handles complex schema types in
OpenAPI documents, which shapes are **fixed** (recognized), which are
**ignored by design**, and why.

## The problem

`Microsoft.OpenApi.Readers` (the official Microsoft reader, pinned to the
1.6.x line because Swashbuckle 7.x depends on Microsoft.OpenApi 1.6) has two
gaps that surface as *"can't recognize complex type"*:

1. **Shape gap** — the reader parses complex schemas fine, but only a *direct*
   `$ref` carries a usable type name. Real-world generators wrap references:
   Swashbuckle emits nullable references as `allOf: [{$ref}] + nullable: true`,
   list endpoints use `type: array, items: {$ref}`, and unions use
   `oneOf`/`anyOf`. Previously all of these produced `schema_id = null` in the
   generated Terraform — the type identity was silently lost.

2. **Version gap** — the 1.6 reader rejects OpenAPI **3.1** documents outright
   (`specification version '3.1.1' is not supported`). This matters because
   **.NET 10's built-in generator (`Microsoft.AspNetCore.OpenApi`) emits 3.1
   by default**, so converting a freshly built service's spec failed entirely.

## The fix — decision matrix

Recognition is implemented in `OpenApiSchemaInterpreter` and applied to both
request and response representations.

### Recognized (fixed)

| Schema shape | Example | Result |
|---|---|---|
| Direct `$ref` | `{ "$ref": "#/components/schemas/Order" }` | `schema_id = "Order"`, `type_name = "Order"` |
| `allOf` with exactly one `$ref` branch | `{ "allOf": [{ "$ref": ".../Order" }], "nullable": true }` | `schema_id = "Order"` (Swashbuckle's nullable-reference pattern) |
| Array of `$ref` | `{ "type": "array", "items": { "$ref": ".../Order" } }` | `schema_id = "Order"`, `type_name = "Order[]"` |
| `oneOf`/`anyOf` with one effective branch | `{ "oneOf": [{ "$ref": ".../Order" }] }` | `schema_id = "Order"` |
| Nested wrappers of the above | array of allOf-wrapped ref, etc. (bounded depth 4) | resolved recursively |

### Ignored by design

| Schema shape | Why it is ignored |
|---|---|
| `oneOf`/`anyOf` with **multiple** distinct branches | A union has no single nameable type. Picking one branch would be wrong half the time; APIM's `type_name` is a single identifier. |
| `allOf` with **multiple** `$ref` branches | The merge of several schemas is a new, *unnamed* composite — naming it after one parent would be misleading. |
| Inline (anonymous) object schemas | There is no name to recognize. The shape exists only in the document. |

**Ignoring never fails the conversion.** The representation keeps its
`content_type` (which is what APIM routing actually needs) and simply carries
no `schema_id`/`type_name`. Those two fields are *informational* in this
generator — it does not emit APIM schema resources, so a missing name costs
documentation value, not correctness.

### OpenAPI 3.1 compatibility mode

When a document declares `openapi: 3.1.x`, the reader (`OpenApiDocumentReader`)
parses it in **3.0 compatibility mode** (the version field is substituted for
parsing only — input is never modified) and records a warning:

> OpenAPI 3.1 document read in 3.0 compatibility mode — JSON-Schema-only
> keywords (type arrays, $defs, …) are ignored.

This is sound because 3.1 is a JSON-Schema alignment of 3.0: the constructs
this converter consumes — paths, operations, parameters, request/response
content, `$ref`s — are structurally identical. 3.1-only keywords degrade
gracefully:

| 3.1 construct | Behavior in compatibility mode |
|---|---|
| `"type": ["string", "null"]` (nullable type arrays) | Non-fatal diagnostic; parameter/schema falls back to the default `string` type |
| `$defs`, `const`, `prefixItems`, … | Ignored (not consumed by the converter anyway) |
| `examples` (array form) | Ignored |

### Tolerant parsing rule

The parser no longer fails on *any* diagnostic. The rule is:

- **Fatal** — the document could not be read at all, or it produced
  diagnostics **and** yielded no paths (the 1.6 reader is YAML-lenient and
  coerces garbage input into an empty document instead of throwing).
- **Tolerated** — diagnostics alongside a usable paths collection
  (3.1 keywords, vendor extensions, minor spec violations).

Strict checking still exists where it belongs: the `validate_openapi_for_apim`
MCP tool and `POST /api/validate` surface **all** reader diagnostics.

## Where this lives

| Concern | Code |
|---|---|
| Reading + 3.1 compatibility mode | `Services/OpenApi/OpenApiDocumentReader.cs` (the only `Microsoft.OpenApi.Readers` call site, behind `IOpenApiDocumentReader`) |
| Complex-shape recognition | `Services/OpenApi/OpenApiSchemaInterpreter.cs` |
| Application to representations | `Services/OpenApi/ApimConfigurationBuilder.cs` |
| Tests pinning the policy | `tests/.../OpenApi/ComplexTypeRecognitionTests.cs` |

## Future

When the API host's Swashbuckle dependency allows upgrading to
Microsoft.OpenApi 3.x (whose `OpenApiDocument.Parse` natively supports 3.1),
only `OpenApiDocumentReader` needs a new implementation — the compatibility
downgrade is deleted and the interpreter policy stays unchanged.
