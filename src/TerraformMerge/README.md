# Terraform Merge

A Windows desktop app for merging Azure APIM **api_operation** blocks between two
Terraform configs — or between a Terraform config and an OpenAPI document — using
a graph-based, similarity-distance parser. Also runs headless from the command
line.

## Building a single-file executable (one `.exe`, no DLLs)

```powershell
dotnet publish src/TerraformMerge/TerraformMerge.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

The output `publish/TerraformMerge.exe` is fully self-contained — the
.NET runtime and every referenced library are bundled inside the one file. No
loose DLLs, no framework install required on the target machine.

## The window

Three list panes, each holding one API method per row, each with its own file
path box + **…** (browse) + **Load** at the bottom:

| Pane | Merge mode | API mode |
|---|---|---|
| **Original** | Terraform config (the base you edit + save) | Terraform config |
| **Diff** | computed changes, or a loaded third file | computed changes |
| **Target** | Terraform config | **OpenAPI** document |

- **Mode** (top): *Merge* aligns two Terraform configs; *API* aligns the
  Terraform Original against an OpenAPI Target.
- **Compute Diff** aligns Original vs Target on the similarity graph and lists
  what would be **[+] added** or **[~] changed**.
- **◄ Add to Original** copies selected operations from Diff/Target into Original
  (skipping equivalents already present).
- **Remove from Original** drops selected rows.
- **Save Original** rewrites the Original file's `api_operations` array from the
  rows currently in the Original pane. Rows that came from the Original file are
  reproduced **byte-for-byte**; rows added from elsewhere are generated as
  canonical blocks in the file's style.
- **Convert OpenAPI…** runs an OpenAPI → Terraform conversion to a new file.

## The parser and matcher (graphs + similarity distance)

Each `api_operation` (or OpenAPI operation) becomes a **node** in a graph. The
matcher builds the complete bipartite similarity graph between two node sets and
solves the **maximum-weight assignment** with the Hungarian (Kuhn–Munkres)
algorithm — a globally optimal alignment, so a locally attractive pair never
steals a partner a better global assignment needs. Similarity blends:

- **method** (0.35), **URL** (0.40), **operationId** (0.15), **parameter set** (0.10);
- components with no evidence (both ids empty, or neither has parameters) are
  dropped and the weights renormalized;
- URL similarity collapses parameter names (`{id}` ≡ `{userId}` ≡ `:id`) and
  blends segment Jaccard with edit distance;
- operationId comparison strips `${...}` interpolations and `{tag}` placeholders,
  so environment-suffixed ids align across `dev`/`prod`.

**Identity gates.** An APIM operation is identified by `(method, url_template)`,
so both are *necessary* conditions — no amount of agreement elsewhere lets a
pair through if it fails one of:

| Gate | Rejects |
|---|---|
| route similarity ≥ `0.34` | `GET /users` vs `GET /orders` (scored `0.60`) |
| same method | `GET /users` vs `POST /users` (scored `0.61`) |
| same segment count | `GET stock/{sku}` vs `GET stock/{sku}/history` (scored `0.72`) |

Each gate exists because of a real defect: without it the aligner reported a
genuinely new operation as "already present", so the merge silently dropped it
instead of offering it as an addition. Segment count is a separate gate rather
than a higher similarity floor because a sub-route (`0.58`) and a legitimate
rename such as `v1/users` → `v2/users` (`0.60`) are otherwise indistinguishable.

**Multiple api groups.** `backend_apis` is a map, so a file may hold several api
groups and each owns its own `api_operations` array. The Original pane lists
every group's operations together, and saving rewrites each group separately:
operations return to the group they were read from, and newly added ones go to
the group most of the retained operations came from.

Terraform blocks are parsed on the shared AST HCL parser (from
`TerraformApi.Application`), so each block is structured exactly — a `<method>`
tag inside a policy heredoc is never mistaken for an operation field.

## Command line

Run the exe with arguments (it attaches to the parent console):

```powershell
# OpenAPI (file or URL) -> APIM Terraform. Omitted settings become {placeholder} tags.
TerraformMerge convert --openapi swagger.json --out apim.tf --env dev --api-group my-api-group

# Append operations from a Target (Terraform or OpenAPI) into an Original Terraform config.
TerraformMerge merge --original apim.tf --target new-swagger.json --out apim.tf --threshold 0.55

TerraformMerge help
```

Run with **no arguments** to open the graphical window.

## Sample files to try

The `samples/` folder holds ready-to-load files:

| File | Use |
|---|---|
| `orders-dev.tf` | A nested APIM config (dev) — load as **Original** |
| `orders-staging.tf` | The staging config with an extra `cancel-order` op — load as **Target** in *Merge* mode |
| `orders-openapi.json` | The Orders OpenAPI spec — load as **Target** in *API* mode |
| `azure-apim-platform.tf` | A harder, more realistic Azure APIM config — load as **Original** |

Try: load `orders-dev.tf` into Original and `orders-openapi.json` into Target
(API mode), **Compute Diff** → the PUT and DELETE `/orders/{orderId}` operations
appear as `[+]` additions; select them, **◄ Add to Original**, **Save Original**.

`azure-apim-platform.tf` is the one to reach for when testing the tool itself
rather than the happy path. It is a *module input* file (`key = value`
assignments), **not** resource-style HCL — `resource "azurerm_api_management_api"
"x" { … }` blocks are not parsed. It carries two api groups, `${var.…}`
interpolations throughout, an indented (`<<-`) policy heredoc whose `<method>`
tags must not be read as operations, interleaved comments, and near-miss
operations (`v1/payments` vs `v2/payments`, `GET` vs `PUT` on the same route).
Every one of those broke something real the first time it was loaded — see the
identity gates above and the multi-group note.

Its round trip is asserted byte-for-byte in the test suite, so it doubles as the
regression fixture: the file you load is the file under test, linked into the
test project rather than copied.

## Tests

```powershell
dotnet test tests/TerraformMerge.Tests/TerraformMerge.Tests.csproj
```

Covers the distance primitives, URL normalization, the similarity scorer, the
Hungarian aligner (including a greedy-trap case and a brute-force optimality
check), the loaders, the HCL block builder, the byte-faithful rewriter, and
end-to-end merges over the shipped samples.

`AlignmentScenarioTests` is data-driven over
`Fixtures/alignment-scenarios.json`. That fixture has **not** been generated
yet — the test currently runs its no-fixture sentinel path and asserts nothing
about alignment. Drop a scenario file in to activate it.
