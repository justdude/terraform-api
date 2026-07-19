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
so both are *necessary* conditions — a different method, or a route similarity
below `0.34`, caps the score under the match threshold no matter how much else
agrees. This matters for merges: without the gates `GET /users` scored `0.60`
against `GET /orders`, and `GET /users` scored `0.61` against `POST /users`, so a
genuinely new operation was treated as "already present" and silently dropped
instead of being offered as an addition.

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

Try: load `orders-dev.tf` into Original and `orders-openapi.json` into Target
(API mode), **Compute Diff** → the PUT and DELETE `/orders/{orderId}` operations
appear as `[+]` additions; select them, **◄ Add to Original**, **Save Original**.

## Tests

```powershell
dotnet test tests/TerraformMerge.Tests/TerraformMerge.Tests.csproj
```

Covers the distance primitives, URL normalization, the similarity scorer, the
Hungarian aligner (including a greedy-trap case), the loaders, the HCL block
builder, the byte-faithful rewriter, and a data-driven suite of adversarial
alignment scenarios (`Fixtures/alignment-scenarios.json`) generated and
independently verified by a 50-agent hardening pass.
