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

### Editing an operation (accept values from either side)

**Double-click any row** (in any pane) — or right-click → **Edit…** — to open the
field editor. Every field is an *editable* combo box whose drop-down lists the
distinct values that field takes across **both** loaded sides. So to move an
operation from staging to dev, double-click it and pick `rg-apim-dev` (offered
because it exists on the other side) for the resource group — or type a new value.

- `operation_id` is shown but **fixed** — it is the operation's identity and is
  never merged across sides. The one thing that rewrites it is an environment
  move (below), which re-stamps its own environment segment.
- Editing an **Original** operation rewrites only the changed line in the file:
  its request/response blocks, policy heredoc, and comments are preserved
  byte-for-byte. `Save Original` persists the change.
- Editing an operation you then **◄ Add to Original** carries your chosen
  api/resource-group/apim values into the generated block instead of the file's
  defaults.

### Environments (dev → qa)

Every pane has an **Env:** picker under its list. It answers "which environment
should these operations be in?" and re-stamps the rows you select:

- `apim_resource_group_name`, `apim_name`, `api_name` — taken from the
  destination environment's **own values** where the loaded files have them
  (the qa file's actual names), otherwise the operation's own value with its
  environment segment rewritten (`rg-apim-dev` → `rg-apim-qa`);
- `operation_id` — its environment segment is rewritten
  (`list-orders-dev` → `list-orders-qa`). This is the one thing that rewrites
  the id: it is not taken from another operation, and an APIM id must be unique
  per instance, so a dev id inside the qa config is a bug;
- `display_name` and `description` — same segment rewrite.

Method, URL template and status code are never touched — they are the
operation's route, not its environment. Interpolated values
(`${var.resource_group}`) are left alone: they are already environment-neutral.

A name only counts as an environment when it is a whole segment of a value, so
`api-devices` is not "dev" and `latest` is not "test". You can type an
environment no loaded file uses yet (an empty qa config); then every value is
derived by rewriting the operation's own.

**The dev → qa flow.** Load the qa config as **Original** and the dev config as
**Target**. Both panes preselect the environment their document is in, so the
Original pane reads `qa`. **Compute Diff** lists what qa is missing, select
those rows and press **◄ Add to Original**: each one is copied — the Target pane
keeps showing its own dev values — and the copy is re-stamped as `qa`.
**Save Original** writes them into the qa file. Rows are labelled with their
environment (`GET  orders  (list-orders-qa)  [qa]`), so a stray dev operation in
a qa list is visible at a glance.

To move operations already in a list, select them and press **Set selected**, or
right-click → **Set environment** ▸. On the Original pane that edits the loaded
file through the AST, so **Save Original** rewrites only those operations' lines
and leaves the rest of the file byte-for-byte. The row editor
(double-click) has the same picker at the top: choosing an environment there
previews the whole operation, `operation_id` included, before you accept it.

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

# Give the qa config the operations it is missing from dev, as qa operations.
TerraformMerge merge --original qa.tf --target dev.tf --env qa

TerraformMerge help
```

`merge --env <e>` is the headless form of the window's "add to Original as qa":
every operation it appends is re-stamped for `<e>` — `apim_resource_group_name`,
`apim_name`, `api_name`, `operation_id`, `display_name`, `description` — by the
same rules as the environment picker above. The original file's own operations
are untouched and re-emitted byte-for-byte.

Without `--env` an appended operation keeps the target's `operation_id`,
`display_name` and `description`, while its `apim_resource_group_name`,
`apim_name` and `api_name` come from the *original* file's api group — a
generated block always blends into the file it is written to. So `--env` is what
moves the id and the environment-suffixed text; the three APIM identifiers it
also pins matter when the original file names more than one environment, or none.

One case to merge **without** `--env`: a destination file that parameterizes those
identifiers (`apim_name = "${var.apim_name}"`). Its own operations are already
environment-neutral, and the generated blocks inherit those interpolations —
whereas `--env qa` writes literal qa values into a file whose house style is
variables.

The flag takes an environment *name*: a bare `--env` (or a value with spaces or
slashes) is rejected with exit 1 and nothing is written — the option parser reads
a valueless flag as the value `true`, and stamping every appended operation with
an environment called "true" is worse than an error.

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
