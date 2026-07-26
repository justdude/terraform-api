# Project context & session handoff

A complete snapshot of the `terraform-api` project and everything built across
this working session. Written so a fresh session (or another engineer) can pick
up with full context.

- **Repo**: `D:\Projects\terraform-api`
- **Branch**: `main`
- **Platform**: Windows 11, .NET 10 SDK (`dotnet` at `C:\Program Files\dotnet`)
- **Latest commit**: `0f90a00` — Fix 12 defects from adversarial review of the OpenAPI 3.1/complex-type work
- **Test suite**: **703 tests green** — 506 Application + 123 MCP + 74 API. 0 build warnings.

> Build/test note: on this machine, prefix shell commands with
> `$env:Path += ";C:\Program Files\dotnet"`. If a Visual Studio debug instance is
> running the API, it locks `bin\` DLLs — stop that process before `dotnet build`.

---

## 1. What the product is

A .NET 10 service that converts **OpenAPI (Swagger) specifications into Azure API
Management (APIM) Terraform configurations**, exposed three ways over the same
engine:

| Surface | Project | Transport |
|---|---|---|
| HTTP API + Swagger UI + web frontend | `TerraformApi.Api` | HTTP/JSON |
| MCP server (11 tools) for AI assistants & CI | `TerraformApi.Mcp` | stdio JSON-RPC |
| NuGet package / library facade | `TerraformApi.Application` | in-process |

Core capabilities: **convert** OpenAPI → APIM Terraform HCL, **append-only sync**
of an existing Terraform file with a new spec (never deletes anything),
**analyze** an existing file, **template profiles** (templatize ⇄ resolve),
**environment transform** (dev→staging→prod), **product generation**, **OpenAPI
naming validation**, and **operations listing** from either side.

---

## 2. Architecture (Clean Architecture)

```
src/
  TerraformApi.Domain/        # Models, interfaces, validation rules — ZERO external deps
  TerraformApi.Application/    # All business logic + services (NuGet-packable)
  TerraformApi.Api/           # MVC controllers + Swagger + static wwwroot frontend
  TerraformApi.Mcp/           # MCP server (stdio), Tools/*.cs, appsettings.json
tests/
  TerraformApi.Application.Tests/   # 506 tests
  TerraformApi.Mcp.Tests/           # 123 tests (incl. real-process stdio integration)
  TerraformApi.Api.Tests/           # 74 tests (WebApplicationFactory integration)
docs/                         # sync-policies, mcp-usage, openapi-complex-types, this file
scripts/                      # Invoke-McpTool.ps1, convert-after-build.ps1
```

Solution file: `terraform-api.slnx` (XML format). DI wiring:
`TerraformApi.Application/DependencyInjection.cs` → `AddApplicationServices()`.

---

## 3. The APIM Terraform Sync Engine (the core feature)

Built in phases 0–10. An **HCL AST** layer parses/writes Azure APIM Terraform
with byte-for-byte format preservation, and an **append-only synchronizer**
merges OpenAPI operations into an existing file.

### HCL AST layer (`Application/Services/Hcl/`)
- `HclLexer` — tokenizes the APIM HCL subset (objects, arrays, heredocs,
  `${...}` interpolations, comments, `$$` escapes). Records source spans.
- `HclParserService` (`IHclParser`) — recursive-descent parser; preserves
  comments and per-node source offsets.
- `HclWriterService` (`IHclWriter`) — format-preserving writer: **unmodified
  nodes are emitted from their original source slice**, only new/dirty nodes are
  re-rendered canonically. Re-indents clean multi-line slices when their parent
  is re-rendered (heredocs never shifted — closing marker stays at column 0).
- AST models in `Domain/Models/Hcl/*` (HclDocument/Object/Array/Assignment/
  Comment/Literal/Interpolation/Heredoc/…).

### Sync engine (`Application/Services/Sync/`, `Application/Services/Apim/`)
- `ApimTerraformReaderService` (`IApimTerraformReader`) — extracts api groups /
  api blocks / operations from the AST, keyed by `(resource group, api name)`
  via `ApisByGroupKey`. Never mistakes `<method>` inside a policy heredoc for an
  operation field.
- `ApimTerraformWriterService` (`IApimTerraformWriter`) — `BuildFromConfiguration`
  builds a fresh AST from an `ApimConfiguration` using a template profile;
  `NeedsQuotedKey` quotes non-identifier group keys (`${...}`, `{tag}`, dots).
- `OperationMatcherService` (`IOperationMatcher`) — fingerprints operations and
  matches by ordered keys (`MethodAndUrl` → `OperationId` → `Tag`) with a
  resolved-mode fallback (substitutes `${var}` via a variable context).
- `DuplicateDetectorService`, `ApimTemplateProfileDetectorService`,
  `ApimTemplateProfileApplierService`, `TerraformInterpolationResolver`,
  `OperationCommentBuilderService`, `OperationExecutionGraphBuilderService`.
- `AppendOnlySynchronizerService` (`IAppendOnlySynchronizer`) — the heart:
  appends OpenAPI-only ops (in the file's detected templating style, with
  `# METHOD URL | op_id: ...` comment blocks + a `REPLACE BEFORE APPLY` header),
  preserves Terraform-only ops, enriches matched ops only where fields are
  missing (`operation_id`/`method`/`url_template` never modified). Honors
  `MarkDeprecated`; `Remove` is refused (append-only invariant). **Nothing is
  ever deleted.**
- Orchestrators: `ConversionOrchestratorService` (`IConversionOrchestrator` —
  Convert/Update) and `SyncOrchestratorService` (`ISyncOrchestrator` —
  Sync/Analyze/ApplyProfile, attaches an `OperationExecutionGraph`).

Default merge policies documented in **`docs/sync-policies.md`**.

---

## 4. OpenAPI facade & reader (`Application/Services/OpenApi/`)

Single facade over all OpenAPI functionality (rewrite in commit `63b5f6b`):

- `OpenApiFacadeService` — **one instance** implementing **both**
  `IOpenApiParser` (→ `ApimConfiguration`) and `IOpenApiOperationsFetcher` (→
  `OperationsListResult`). DI maps both interfaces to the same singleton.
- `OpenApiDocumentReader` (`IOpenApiDocumentReader`, injectable since `4ce7bec`)
  — the **only** `Microsoft.OpenApi.Readers` call site. Never throws; collects
  diagnostics. Pinned to Microsoft.OpenApi **1.6.28** (Swashbuckle 7.x depends
  on 1.6.x). Future migration to Microsoft.OpenApi 3.x (`OpenApiDocument.Parse`,
  native 3.1) is a one-file swap.
- `ApimConfigurationBuilder`, `OperationsListBuilder` — static mapping helpers.
- `OpenApiSchemaInterpreter` — resolves complex schema shapes into
  `schema_id`/`type_name`. Full policy in **`docs/openapi-complex-types.md`**.

### Complex types & OpenAPI 3.1 (commits `02e9319`, `0f90a00`)
- **Complex-type recognition**: direct `$ref`, `allOf`-wrapped single ref,
  inline arrays (`Order[]`), single-branch `oneOf`/`anyOf` are RECOGNIZED;
  multi-branch unions / multi-`$ref` `allOf` / inline schemas are IGNORED (kept
  as content-type only, never fail conversion). Array-ness tracked through the
  recursion — a bare `$ref` to an array component does **not** get a spurious
  `[]`, and a wrapped array keeps it.
- **OpenAPI 3.1 compatibility mode**: .NET 10 emits 3.1 by default, but the 1.6
  reader rejects it. The reader down-levels a **root-anchored** 3.1 declaration
  (JSON via `JsonDocument`, YAML via a column-0 regex) to 3.0.3 for parsing and
  sets `DowngradedFrom31` + a warning. A `3.1` string inside an example never
  triggers a downgrade.
- **Tolerant parsing**: reader errors are fatal **unless** the doc was
  down-leveled from 3.1 (where 3.1-only keywords legitimately produce
  diagnostics). Genuine 3.0 spec violations (e.g. a parameter missing its
  required `name`) stay fatal. Nameless parameters are skipped by the builders.
- **Warning surfacing**: the 3.1 compat warning is threaded to every caller —
  `ConversionResult.Warnings`, `SyncReport.Warnings`, fetch-operations
  `warnings`, and `ValidateResponse.Warnings` / the validate tool's `Warnings:`
  section.

---

## 5. Placeholder tags (commit `dda4224`)

Every APIM setting is **optional**. Missing values become replaceable tags
(`{api-group}`, `{environment}`, `{stage-group-name}`, `{apim-name}`,
`{api-path-prefix}`, `{api-path-suffix}`, `{api-gateway-host}`,
`{backend-service-path}`, plus `{product-id}`, `{product-display-name}`).
`Domain/Models/ApimPlaceholders.cs` normalizes settings and builds the
explanatory header comment prepended to generated files. Naming validation and
sanitization are placeholder-aware. Group keys with tags are quoted
(`"{api-group}" = {`) so output stays valid HCL (fixed in `ba71498`).

---

## 6. API host (`TerraformApi.Api`) — controllers + Swagger

Converted from Minimal API to **MVC controllers** (`a28dbb6`). Swagger UI at
`/swagger` (Swashbuckle 7.2.0). Automatic model validation is suppressed to keep
the project's own error shapes. Controllers:
- `ConversionController` — `/api/convert`, `/api/convert/update`,
  `/api/transform-environment`, `/api/fetch-operations`,
  `/api/parse-terraform-operations`, `/api/validate`, `/api/environments`,
  `/api/health`.
- `SyncController` — `/api/sync`, `/api/analyze-terraform`,
  `/api/apply-template-profile`.
- `ProductsController` — `/api/generate-product`.

Static frontend in `wwwroot/` (index.html + css/js). SPA fallback is
**regex-constrained with a `:nonfile` filter** so `/api`, `/swagger`, and any
file-looking URL (`/css/*.css`) never fall back to `index.html` — a missing
asset returns a clean 404 (fixes the unstyled-page bug, `e94cb12`; and the
Swagger "invalid version field" cache bug, `70babf9`). Fallback HTML is
`Cache-Control: no-store`.

**Deploy note**: use `dotnet publish` — `bin/Debug` does NOT contain `wwwroot`.

---

## 7. MCP server (`TerraformApi.Mcp`) — 11 tools

stdio JSON-RPC, `[McpServerToolType]`/`[McpServerTool]` auto-discovered via
`WithToolsFromAssembly()`. Every HTTP endpoint has a 1:1 MCP tool backed by the
same Application service (parity table in README):

| MCP tool | HTTP endpoint | Service |
|---|---|---|
| `convert_openapi_to_terraform` | `/api/convert` | `IConversionOrchestrator.Convert` |
| `update_terraform_from_openapi` | `/api/convert/update` | `IConversionOrchestrator.Update` |
| `sync_openapi_with_terraform` | `/api/sync` | `ISyncOrchestrator.Sync` |
| `analyze_terraform_apim` | `/api/analyze-terraform` | `ISyncOrchestrator.Analyze` |
| `apply_template_profile` | `/api/apply-template-profile` | `ISyncOrchestrator.ApplyProfile` |
| `transform_environment` | `/api/transform-environment` | `IEnvironmentTransformer` |
| `fetch_openapi_operations` | `/api/fetch-operations` | `IOpenApiOperationsFetcher` |
| `parse_terraform_operations` | `/api/parse-terraform-operations` | `ITerraformOperationsParser` |
| `validate_openapi_for_apim` | `/api/validate` | naming validator + reader |
| `generate_apim_product` | `/api/generate-product` | `IApimProductGenerator` |
| `list_environment_presets` | `/api/environments` | `IOptions<ApimEnvironments>` |
| (none) | `/api/health` | HTTP liveness only |

Both hosts resolve `openApiJson`/`openApiUrl` through the shared
`OpenApiDocumentResolver` (10 MB cap, JSON validation, cancellation).

---

## 8. Headless automation (`scripts/`, docs in `docs/mcp-usage.md`)

- `Invoke-McpTool.ps1` — call any MCP tool over stdio without an AI client.
- `convert-after-build.ps1` — OpenAPI file/URL → Terraform in one call; designed
  for an MSBuild `AfterTargets="Build"` target or a CI step. Exits non-zero on
  failure. (PS 5.1 gotcha handled: use `[IO.File]::ReadAllText`, not
  `Get-Content -Raw`, when composing tool-argument JSON.)

---

## 9. NuGet packaging (commit `63b5f6b`)

`TerraformApi.Application` + `TerraformApi.Domain` are packable (`dotnet pack`
verified). Single library entry point: **`TerraformApiFacade`** —
`TerraformApiFacade.Create()` wires the whole engine with no DI/host; covers
convert, update, sync, analyze, apply-profile, products, transform, and both
operation listings. Also registered in `AddApplicationServices()` for DI hosts
(which now `TryAdd` a `NullLogger<>` fallback so a bare `ServiceCollection`
works).

---

## 10. Documentation index

- `README.md` — features, parity table, placeholder tags, NuGet usage, deploy.
- `docs/sync-policies.md` — append-only merge policy defaults + rationale.
- `docs/mcp-usage.md` — with-MCP / without-MCP / headless automation.
- `docs/openapi-complex-types.md` — complex-type recognition + 3.1 policy.
- `docs/PROJECT_CONTEXT.md` — this file.

---

## 11. Session commit history (newest first)

```
0f90a00 Fix 12 defects from adversarial review of the OpenAPI 3.1/complex-type work
02e9319 Fix complex-type recognition + OpenAPI 3.1 compatibility mode
4ce7bec Make OpenApiDocumentReader non-static (injectable behind an interface)
63b5f6b OpenAPI facade rewrite + NuGet packaging (ACC1-ACC4)
e94cb12 Fix missing styling on deployed machines: assets never fall back to HTML
49d2e4a MCP integration tests, headless automation scripts, usage docs
ba71498 Fix unquoted placeholder group keys producing unparseable HCL
dda4224 Placeholder-tag defaults, product generation API+tool, parity docs
70babf9 Fix Swagger UI "definition does not specify a valid version field"
4d9a9a6 Code review fixes: sync URL parity, lexer escape, writer reindent, policies
a28dbb6 Convert API to controllers + Swagger UI, align MCP on shared resolver
fd6402a Iteration: ExecutionGraph, I3 perf test, MCP verification, doc polish
7e2d74d Phase 10: final acceptance scenarios + documentation
e1ec161 Phase 9: API endpoints + MCP tools for sync engine
4b91a7e Phase 8: SyncOrchestrator with Sync/Analyze/ApplyProfile
67505a6 Phase 7: AppendOnlySynchronizer — core append-only sync engine
e54f443 Phase 6: DuplicateDetector
6dd879c Phase 5: OperationMatcher with resolved-mode fallback
af9e162 Phase 4: InterpolationResolver, ProfileDetector, CommentBuilder tests
0f13dee Phase 3: ApimTerraformReader + ApimTerraformWriter
1cc7ef6 Phase 2: Sync domain models
2c191c7 Phase 0+1: HCL AST, lexer, parser, writer with format preservation
8f5009e Baseline: consistency fixes, unified operations format, URL support
```

---

## 12. Notable engineering decisions & fixed regressions

- **API↔MCP parity** is enforced by design: both call the same Application
  service; `/api/validate` defaults `environment` to `dev` like the tool.
- **`update_terraform_from_openapi` does NOT delegate to sync** — delegation
  would change the legacy output format; it points users at
  `sync_openapi_with_terraform` in its description instead.
- **`Sync/Analyze/ApplyProfile` live on a new `ISyncOrchestrator`**, not on
  `IConversionOrchestrator`, to avoid breaking the latter's direct constructions.
- **Adversarial code reviews** (via the Workflow tool) caught real regressions
  self-introduced in the same session — most notably the tolerant-parsing rule
  (`0f90a00`) that would have emitted invalid Terraform for 3.0 specs with a
  structural error, invisible to the existing tests.

## 13. Known limitations / candidate next steps

- Microsoft.OpenApi pinned to 1.6.x (Swashbuckle constraint) → 3.1 handled via
  compatibility mode. Upgrading to Microsoft.OpenApi 3.x would make 3.1 native
  and delete the downgrade — only `OpenApiDocumentReader` changes.
- Phase 10's optional web-UI "Sync" tab was intentionally deferred (separate PR).
- The sync core is synchronous/CPU-bound but proven < 1 s at 60 ops / 5 groups
  (the I3 performance test).
- MCP servers requiring OAuth (github/slack/etc.) are not authorized in
  non-interactive sessions — authorize via claude.ai connector settings or
  `claude mcp` / `/mcp` interactively.

---

## 14. Quick commands

```powershell
$env:Path += ";C:\Program Files\dotnet"

# Build + test everything
dotnet build D:\Projects\terraform-api\terraform-api.slnx
dotnet test  D:\Projects\terraform-api\terraform-api.slnx

# Run the API (Swagger at https://localhost:7166/swagger)
dotnet run --project src\TerraformApi.Api --launch-profile https

# Pack the NuGet packages
dotnet pack src\TerraformApi.Application -c Release -o .\artifacts

# Headless conversion via the MCP server (no host/AI client)
.\scripts\convert-after-build.ps1 -OpenApi .\swagger.json -OutputPath .\apim.tf -Environment dev
```
