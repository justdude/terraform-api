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

Array-ness is tracked **through** the recursion (not re-derived from the
top-level node), so the `[]` suffix follows the element wherever the array
sits, and a bare `$ref` to an array component never gains a spurious one.

### Recognized (fixed)

| Schema shape | Example | Result |
|---|---|---|
| Direct `$ref` | `{ "$ref": ".../Order" }` | `schema_id = "Order"`, `type_name = "Order"` |
| Direct `$ref` to an **array component** | `OrderList = {type:array,items:{$ref:Order}}`, body `{ "$ref": ".../OrderList" }` | `schema_id = "OrderList"`, `type_name = "OrderList"` (the component name already denotes the array — **no** `[]`) |
| `allOf` resolving to one named type | `{ "allOf": [{ "$ref": ".../Order" }], "nullable": true }` | `schema_id = "Order"` (Swashbuckle's nullable-reference pattern; also single-base inheritance) |
| Inline array of `$ref` | `{ "type": "array", "items": { "$ref": ".../Order" } }` | `schema_id = "Order"`, `type_name = "Order[]"` |
| Array wrapped in `oneOf`/`anyOf`/`allOf` | `{ "oneOf": [{ "type":"array", "items": {"$ref":".../Order"} }] }` | `schema_id = "Order"`, `type_name = "Order[]"` (suffix preserved) |
| `oneOf`/`anyOf` where **every** branch is the same type | `{ "anyOf": [{ "$ref": ".../Order" }] }` | `schema_id = "Order"` |
| Nested wrappers of the above | array of allOf-wrapped ref, etc. (bounded depth 4) | resolved recursively |

### Ignored by design

| Schema shape | Why it is ignored |
|---|---|
| `oneOf`/`anyOf` where branches differ **or any branch is anonymous** | A union of e.g. `Order` + an inline schema has no single nameable type. It is ignored (not mislabeled as the named branch). |
| `allOf` merging several **distinct** `$refs` | The merge of several schemas is a new, *unnamed* composite — naming it after one parent would be misleading. |
| Inline (anonymous) object schemas | There is no name to recognize. The shape exists only in the document. |

**Ignoring never fails the conversion.** The representation keeps its
`content_type` (which is what APIM routing actually needs) and simply carries
no `schema_id`/`type_name`. Those two fields are *informational* in this
generator — it does not emit APIM schema resources, so a missing name costs
documentation value, not correctness.

### OpenAPI 3.1 compatibility mode

When a document declares `openapi: 3.1.x` **at the document root**, the reader
(`OpenApiDocumentReader`) parses it in **3.0 compatibility mode** and records a
warning:

> OpenAPI 3.1 document read in 3.0 compatibility mode — JSON-Schema-only
> keywords (type arrays, $defs, …) are ignored.

Version detection is **root-anchored and format-agnostic**:

- **JSON** — the root `openapi` property is read with `JsonDocument`; a `3.1`
  string buried in an `example`/`default` of a genuine 3.0 document never
  triggers a downgrade.
- **YAML** — a top-level (`column 0`) `openapi: 3.1.x` line is recognized too,
  so a pasted YAML 3.1 spec down-levels the same as its JSON form.

The version is substituted for parsing only — the input text is never returned
modified.

The downgrade sets `OpenApiReadResult.DowngradedFrom31`, which the tolerant
rule below uses.

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

Diagnostics are tolerated **only for down-leveled 3.1 documents**, where
legitimate 3.1-only keywords (`type: ["string","null"]`, `$defs`, …) surface as
non-fatal diagnostics after the downgrade. The rule is:

- **Fatal** — the document could not be read at all, or it produced reader
  errors and was **not** a 3.1 downgrade. A genuine 3.0 spec violation (e.g. a
  parameter missing its required `name`) stays fatal, so the converter never
  emits invalid Terraform.
- **Tolerated** — errors on a document that was down-leveled from 3.1.

As defense in depth, a parameter without a `name` is **skipped** by the builders
(rather than emitted as an invalid `name = ""` block) even on the tolerated path.

### Surfacing the compatibility warning

The 3.1 compatibility note is threaded to every caller, not just logged:

| Path | Where it appears |
|---|---|
| Convert / Update | `ConversionResult.Warnings` → `/api/convert` response, `convert_openapi_to_terraform` output |
| Sync | `SyncReport.Warnings` |
| Fetch operations | `OperationsListResult.Warnings` → `fetch_openapi_operations` `warnings`, `/api/fetch-operations` |
| Validate (strict, all-diagnostics) | `ValidateResponse.Warnings` and a `Warnings:` section in `validate_openapi_for_apim` |

`ApimConfiguration.Warnings` and `OperationsListResult.Warnings` carry the note
out of the facade; `OpenApiReadResult.DowngradedFrom31` gates the tolerance.

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
