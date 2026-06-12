# Append-only sync — default merge policies

This document lists every default policy applied by the sync engine
(`POST /api/sync` / `sync_openapi_with_terraform`) and the reasoning behind it.
All defaults express **append-only semantics**: nothing existing is ever
modified or removed; only missing data is added.

## Operation-level policies

| Situation | Default | Why |
|---|---|---|
| Operation in Terraform but not in OpenAPI | `Preserve` | Append-only invariant: manually-added or legacy operations must survive a sync even when the spec doesn't mention them. |
| Operation in OpenAPI but not in Terraform | `Append` | The whole point of sync — new spec operations are added to the end of `api_operations`, in the templating style detected from the file. |
| Operation matched ambiguously (several candidates) | nothing applied + `AmbiguousMatch` warning | Acting on an ambiguous match risks duplicating an existing operation; a human must decide. |

## Per-field policies (matched operations)

| Field | Default | Why |
|---|---|---|
| `operation_id` | `Preserve` | Identity field. Changing it would recreate the APIM resource. |
| `method` | `Preserve` | Routing contract. A changed method is a different operation, not an update. |
| `url_template` | `Preserve` | Routing contract, same as method. |
| `display_name` | `EnrichIfMissing` | Cosmetic; filled from the OpenAPI summary only when the Terraform value is empty/missing. |
| `description` | `EnrichIfMissing` | Documentation; filled only when empty/missing. |
| `status_code` | `EnrichIfMissing` | Filled only when empty/missing. |

`EnrichIfMissing` treats these as missing: field absent, empty string literal `""`, or `null`.

Override any field via `operationFieldOverrides` (API) or
`operationFieldOverridesJson` (MCP): `{"url_template": "Overwrite"}`.
Available values: `Preserve` · `EnrichIfMissing` · `Overwrite`.

## Collection policies (matched operations)

| Collection | Default | Why |
|---|---|---|
| `request.header` | `AppendMissing` | New headers from the spec are added (matched by name, case-insensitive); existing entries are never touched. |
| `request.query` | `AppendMissing` | Same as headers. |
| `request.template` | `AppendMissing` | Same as headers. |
| `responses` | `AppendMissing` | New response codes are added (matched by `status_code`); existing entries are never touched. |
| `responses.header` | `AppendMissing` | Same. |
| `responses.representation` | `AppendMissing` | Same. |

`Replace` exists in the enum but is rejected in sync flows by design.

## API-block policies

All `api = [{ ... }]` fields default to `Preserve`:
`name`, `display_name`, `path`, `service_url`, `policy`, `protocols`, `revision`.

The API block describes infrastructure identity; sync never rewrites it.

## Match strategy

Default key order: `MethodAndUrl` → `OperationId` → `Tag`.

- URLs are normalized before comparison: trailing slash trimmed, duplicate
  slashes collapsed, `:param` unified to `{param}`, leading slash optional.
  Path case **matters** by default; parameter **names** matter by default.
- Matching is **structural** first: `"${operation_path}"` only equals
  `"${operation_path}"`. When a `variableContext` is provided, unmatched
  operations get a second pass in **resolved mode** (variables substituted).

## Template profiles

| Profile | Use |
|---|---|
| `Auto` (default) | Detect the style from the existing file. Highly templated file → new operations templated the same way; literal file → literal operations. |
| `UserExampleProfile` | The canonical placeholder set: `${stage_group_name}`, `${apim_name}`, `${api_name}-${env}`, `${operation_prefix}-${env}`, … |
| `ExtendedProfile` | Adds `${api_version}`, `${backend_url_protocol}`, `${subscription_required}` and per-operation `{op}` substitution in `operation_id`. |
| `LiteralProfile` | No placeholders — everything literal. |

## Output guarantees

- Unmodified nodes are emitted **byte-for-byte** from the original source
  (the parser records source spans; the writer re-renders only dirty nodes).
- Every inserted operation gets a 2–3 line leading comment:
  `# METHOD URL | op_id: ID`, then `display_name/source/inserted date`,
  then (when placeholders exist) `placeholders to replace: ...`.
- A `REPLACE BEFORE APPLY` header is maintained (never duplicated) before
  `api_operations` whenever inserted operations reference `${...}` variables.
