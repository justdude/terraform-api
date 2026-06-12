# Implementation Roadmap: APIM Terraform Sync Engine (append-only)

> **Audience**: LLM ( Claude Opus ) and a senior .NET developer implementing the feature.
> **Context**: Extending existing project `terraform-api` (.NET 10, Clean Architecture: Domain / Application / Api / Mcp).
> **Goal**: Add an engine that can generate APIM Terraform from scratch from OpenAPI and (b) ** append - only ** sync existing Terraform config with current OpenAPI , **never deleting anything**, plus detect duplicates by multiple keys and provide a full report.

> ⚠ ️ ** REVISION 1 (2026-06-12)** — added:
> - **§ REV -1.2** — Complete list of placeholders and ` ApimTemplateProfile ` (which we template when generating from OpenAPI ).
> - **§ REV -1.3** — Detection of the style of an existing file + auto - grouping by `( apim _ resource _ group _ name , api _ name )`.
> - **§ REV -1.4** — Grouping of parsed APIs .
> - **§ REV -1.5** — Support for comments in AST + comment format before each operation.
> - **§ REV -2** - Detailed list of changes to the MCP server: 3 new tools + updates to existing ones.
> - All basic sections of the plan (§1–§9) remain in force, REVISION 1 **supplements** them and **clarifies** them in several places.

---

## 0. Context and constraints that change design

### 0.1. The structure of the target HCL is not planar

In the working example provided by the user, the configuration looks like this:

```hcl
apis = {
bpc_apis = {
backend_apis = {
"${api_group_name}" = {
product = []
api = [ { ... } ]
api_operations = [ { ... }, { ... } ]
}
}
}
}
```

This is a three-level nesting under a named key (apis.bpc_apis.backend_apis.<api_group>), and the values inside are arrays of objects. The current generator (TerraformGeneratorService) and merger (TerraformMergerService) assume a flat top-level <api_group_name> = { ... }—this works for a "scratch" project, but doesn't scale to a real project file.

**Consequence**: the parser must understand an arbitrary path to a node with ` api_operations` and ` api` , rather than searching for them using regular expressions from the beginning of the file .

### 0.2. Terraform interpolations everywhere

All values in the example are interpolations: ` name = "${ api _ name }-${ env }"`, ` operation _ id = "${ operation _ prefix }-${ env }"`, ` url _ template = "${ operation _ path }"`. This means:

- The current regex ` operation _ id \ s *=\ s *"([^"]+)"` will return the entire string `${ operation _ prefix }-${ env }`. This is a valid "token" by itself, but **it is impossible to compare operations between environments using this token** because `${ env }` is different in dev and prod .
- For matching and detection you need **two modes**:
- ** Structural mode ** — compare tokens as is (two operations are "the same" if their HCL expressions are textually identical). Suitable for round - tripping within a single file.
- ** Resolved mode ** — the user provides a ` Dictionary < string , string > ` with variable values (e.g. ` env = dev` , ` api_name = bpc` ), we substitute and compare the already resolved strings. Suitable for synchronizing with a specific environment.

###0.3. Append - only - not merge , but enrichment

The "cannot remove, can only add ` url` parameters, method type, method parameters" restriction is not a classic merge . This is:

- An operation that is not in OpenAPI but is in Terraform → **remains as is, always**.
- An operation that is present in both → **default: do not change anything**; specific fields can be enriched only if the existing Terraform does not contain the field or it is empty** (` EnrichOnly ` policy). Fields that can be "enriched" if signaled by OpenAPI (` request . header [] , ` request . query [] , ` responses [] ) are ** append to collections**, not replace .
- An operation that exists in OpenAPI but not in Terraform → **append the whole thing** ( append at the top level).

This needs to be expressed as a ** per - field policy** + ** per - collection - element policy**, and made configurable.

### 0.4. What's already in the downloaded documentation

Existing files (`OPERATION_TRACKING_*`, `EXECUTIVE_SUMMARY`, `IMPLEMENTATION_ROADMAP`, etc.) yield:
- Operation graph (Node / Graph / Statistics)
- Statuses (`Included | Modified | Excluded | Blocked | Skipped | Deprecated`)
- Tracking report with deltas
- Export to Mermaid / CSV / JSON

We'll use this skeleton as is for reporting. However, everything related to extracting operations from the HCL and matching them will be completely rewritten: the regex approach from the current TerraformMergerService is not applicable.

---

## 1. Solution architecture

### 1.1. Layers and New Modules

```
TerraformApi . Domain
└── Models
├── Hcl / ← NEW: AST HCL
│ ├── HclDocument . cs
│ ├── HclNode . cs ( abstract )
│ ├── HclObject . cs
│ ├── HclArray . cs
│ ├── HclLiteral . cs
│ ├── HclInterpolation . cs
│ ├── HclHeredoc . cs
│ ├── HclAssignment . cs
│ ├── HclComment . cs ← NEW (see § REV -1.5)
    │ └── HclObjectItem.cs (abstract) ← NEW (assignment | comment)
├── Sync/ ← NEW: Synchronization Models
│ ├── OperationFingerprint.cs
│ ├── OperationMatchKey.cs (enum)
│ ├── OperationMatchStrategy.cs
│ ├── FieldMergePolicy.cs (enum)
│ ├── MergePolicy.cs
│ ├── OperationDiff.cs
│ ├── FieldDiff.cs
│ ├── DuplicateGroup.cs
│ ├── SyncReport.cs
│ ├── SyncResult.cs
│ ├── ApimTemplateProfile.cs ← NEW (see §REV-1.2)
│ ├── CorsTemplateVariables.cs ← NEW
│ ├── DetectedProfile.cs ← NEW (see §REV-1.3)
│ ├── StylingConfidence.cs (enum) ← NEW
│ ├── ApimApiGroupKey.cs ← NEW (see §REV-1.4)
│ └── OperationCommentSpec.cs ← NEW (see §REV-1.5)
├── Apim/
│ ├── ParsedApimDocument.cs
│ ├── ParsedApiGroup.cs
│ ├── ParsedApi.cs
│ ├── ParsedApiOperation.cs
│ └── HclValueRef.cs
└── Tracking/ ← from an existing plan
├──OperationExecutionGraph.cs
└── OperationTrackingReport.cs

TerraformApi.Domain
└── Interfaces
├── IHclParser.cs ← NEW
├── IHclWriter.cs ← NEW
├── IApimTerraformReader.cs ← NEW
├── IApimTerraformWriter.cs ← NEW
├── IOperationMatcher.cs ← NEW
├── IDuplicateDetector.cs ← NEW
├── IAppendOnlySynchronizer.cs ← NEW
├── IApimTemplateProfileDetector.cs ← NEW (see §REV-1.3)
├── IApimTemplateProfileApplier.cs ← NEW (see §REV-1.2.5)
├── IOperationCommentBuilder.cs ← NEW (see §REV-1.5)
└── IOperationExecutionGraphBuilder.cs ← from an existing plan

TerraformApi.Application
└── Services
├── Hcl/
│ ├── HclLexer.cs ← NEW
│ ├── HclParserService.cs ← NEW
│ └── HclWriterService.cs ← NEW
├── Apim/
│ ├── ApimTerraformReaderService.cs ← NEW
│ └── ApimTerraformWriterService.cs ← NEW
├── Sync/
│ ├── OperationMatcherService.cs ← NEW
│ ├── DuplicateDetectorService.cs ← NEW
│ ├── AppendOnlySynchronizerService.cs ← NEW
│ ├── TerraformInterpolationResolver.cs ← NEW
│ ├── ApimTemplateProfileDetectorService.cs ← NEW (see §REV-1.3)
│ ├── ApimTemplateProfileApplierService.cs ← NEW
│ └── OperationCommentBuilderService.cs ← NEW (see §REV-1.5)
├── OperationExecutionGraphBuilderService.cs (from existing plan)
└── ConversionOrchestratorService.cs (modified: Sync(), Analyze(), ApplyProfile() added)

src/TerraformApi.Mcp/Tools/ ← updates (see §REV-2)
├── SyncTool.cs ← NEW
├── AnalyzeTool.cs ← NEW
├── ApplyTemplateProfileTool.cs ← NEW
├── ConvertTool.cs ← UPDATE (templateProfile param)
    └── UpdateTool . cs ← UPDATE (delegates to Sync )
```

### 1.2. Data Flow (Scenario 2: Sync )

```
┌───────────────────┐ ┌──────────────────────────┐
│ OpenAPI JSON │ │ Existing HCL │
└────────┬────────┘ └────────────┬───────────┘
│ │
▼ ▼
   OpenApiParser            HclParserService
│ │
▼ ▼
   ApimConfiguration       HclDocument ( AST )
   (operations[]) │
│ ▼
│ ApimTerraformReaderService
│ (pulls out api/api_operations
         │ with preservation of paths in AST )
│ │
│ ▼
│ ParsedApimDocument
│ ├── ApiGroups []
│ │ └── ParsedApiOperation []
│ └── HclDocument (original AST for round - trip )
│ │
└──────┬───────────────────┘
▼
       OperationMatcherService
(according to the selected MatchStrategy )
│
▼
       MatchResult { Added , Existing , Removed - in - Tf - only }
│
▼
       DuplicateDetectorService
(separately for everything in HCL )
                │
▼
AppendOnlySynchronizerService
(applies MergePolicy to AST)
│
▼
Modified HclDocument
│
▼
HclWriterService → string (valid HCL)
│
▼
SyncResult { TerraformConfig, SyncReport, ExecutionGraph }
```

### 1.3. Key architectural decisions

| Solution | Rationale |
|---|---|
| Our own minimal HCL parser, not a third-party library | We need to support a narrow subset (apis.bpc_apis.backend_apis..., heredocs, interpolations). Existing .NET HCL libraries are abandoned/incomplete. Minimal lexer + recursive parser — ~600 lines, fully controllable. |
| The AST is saved in its entirety in HclDocument and reused for writing | Round - trip without losing comments, heredoc formatting , and interpolations. Writer operates on top of the AST , not on model-based reflection . |
| Interpolations are a separate type of node ` HclInterpolation ` | They are not lost during comparison, can be optionally resolved, and can be compared “textually”. |
| ` OperationFingerprint ` — multi-key record | Not a single matching strategy, but a composition of keys. The user chooses the priority. |
| ` MergePolicy ` per - field + per - collection | Append - only cannot be expressed with one flag - separate policies are needed for scalar fields, for collections ( headers , query params , responses ) and for blocks (` request `, ` policy `). |
| The APIM structure parser is **separated** from the HCL parser | The HCL parser knows nothing about APIM . The ` ApimTerraformReaderService` navigates the AST using specific paths. This allows for independent testing of layers. |
| ` OperationExecutionGraphBuilder ` is a consumer, not a source | The graph is built **after** Sync based on ` SyncReport `. There should be no regex inside the builder . |
| The old ` TerraformMergerService` remains as a thin wrapper | We don't break existing `/ api / convert / update` endpoints and MCP tools. They start delegating to ` AppendOnlySynchronizerService` via an adapter. |

---

## 2. Domain Models - Detailed Specification

### 2.1. HCL AST

File: ` src / TerraformApi . Domain / Models / Hcl / HclDocument . cs ` and adjacent.

```csharp
namespace TerraformApi.Domain.Models.Hcl;

/// Root node: either a sequence of assignments at the top level,
/// or a wrapper around one object.
public sealed record HclDocument
{
public List<HclAssignment> RootAssignments { get; init; } = [];
    /// Optional raw source for comment diagnostics / recovery.
    public string? OriginalSource { get; init; }
}

public abstract record HclNode
{
/// 1-based position in the source (for errors).
public int Line { get; init; }
public int Column { get; init; }
}

public sealed record HclAssignment : HclNode
{
public required string Key { get; init; } // for example, "operation_id"
public required HclValue Value { get; init; }
    /// True if the key was in quotes: `" my - api - group " = { ... }`
    public bool KeyIsQuoted { get; init; }
}

public abstract record HclValue : HclNode;

public sealed record HclObject : HclValue
{
public List<HclAssignment> Assignments { get; init; } = [];

public HclValue? Get(string key) =>
Assignments.FirstOrDefault(a => a.Key == key)?.Value;
}

public sealed record HclArray : HclValue
{
public List<HclValue> Items { get; init; } = [];
}

/// String/number/bool/null.
public sealed record HclLiteral : HclValue
{
public required string RawValue { get; init; } // as in the source
public required HclLiteralKind Kind { get; init; }
}

public enum HclLiteralKind { String, Number, Bool, Null }

/// The entire interpolated expression: `${ api _ name }-${ env }` or ` var . foo `.
/// Stored as RawText (with or without curly braces, as it was).
public sealed record HclInterpolation : HclValue
{
/// The full source text between quotes, including `${...}`.
/// Example: `"${ api _ name }-${ env }"` → InnerText = `${ api _ name }-${ env }`.
    public required string InnerText { get; init; }

    /// Extracted variable/expression names in order of appearance.
    /// For `"${api_name}-${env}"` it is [api_name, env].
public IReadOnlyList<string> ReferencedExpressions { get; init; } = [];
}

/// `<<XML ... XML` or `<<-XML ... XML` (indented).
public sealed record HclHeredoc : HclValue
{
public required string Marker { get; init; } // "XML"
public required string Content { get; init; } // as is, without markers
public bool Indented { get; init; } // <<- variant
}
```

**Comparison rules**:
- Two ` HclLiteral ` are equal if ` Kind ` and ` RawValue ` are equal (for String - after unification of quotation marks).
- Two ` HclInterpolation ` are structurally equal when ` InnerText ` is equal (after normalizing spaces within `${...}`).
- ` HclLiteral { String , " foo "}` and ` HclInterpolation { InnerText =" foo "}` are **not equal** (one is literal , the other is interpolation , even if it resolves to ` foo `).

### 2.2. Parsed APIM structure

File: ` src / TerraformApi . Domain / Models / Apim / ParsedApimDocument . cs` .

```csharp
namespace TerraformApi.Domain.Models.Apim;

/// What we took out of HCL on top of AST .
public sealed record ParsedApimDocument
{
/// The original AST that ParsedApiGroup s reference through their paths.
    public required HclDocument Ast { get; init; }

    /// Path from the root to the parent ` api _ group _ name ` blocks
/// (`[" apis " ," bpc_apis "," backend_apis " ]` for a working example ) .
/// null — if the structure is flat (` api _ group _ name = { ... }` immediately at the root).
    public IReadOnlyList<string>? ApiGroupParentPath { get; init; }

public List<ParsedApiGroup> ApiGroups { get; init; } = [];
}

public sealed record ParsedApiGroup
{
public required string ApiGroupName { get; init; } // as it was in HCL (with or without quotes)

/// Reference to the node in Ast (`HclObject`) that contains api/api_operations/product.
public required HclObject AstNode { get; init; }

public List<ParsedApi> Apis { get; init; } = [];
public List<ParsedApiOperation> Operations { get; init; } = [];
}

public sealed record ParsedApi
{
public required HclObject AstNode { get; init; }

    /// Extracted values. Each field is ( Raw , MaybeResolved ).
    public HclValueRef Name { get; init; } = new();
public HclValueRef ServiceUrl { get; init; } = new();
public HclValueRef Path { get; init; } = new();
public HclValueRef? Policy { get; init; } // HclHeredoc if present
// ... other fields according to ApimApi
}

public sealed record ParsedApiOperation
{
public required HclObject AstNode { get; init; }

public required HclValueRef OperationId { get; init; }
public required HclValueRef Method { get; init; }
public required HclValueRef UrlTemplate { get; init; }
public HclValueRef? DisplayName { get; init; }
public HclValueRef? StatusCode { get; init; }
public HclValueRef? Description { get; init; }

    /// Request / response subobjects — leave them as raw references to the AST ,
/// so that the merge of their parameters is an operation on AST arrays .
    public HclArray? RequestArray { get; init; }
public HclArray? ResponsesArray { get; init; }
}

/// A convenient wrapper around the value in the AST : gives both the raw node and
/// "best text representation" for comparison.
public sealed record HclValueRef
{
public HclValue? Node { get; init; }

/// Text for structural comparison: for literal — RawValue;
/// for interpolation — the entire `${...}`; for heredoc — Content.
public string? StructuralText =>
Node switch
{
HclLiteral l => l.RawValue,
HclInterpolation i => i.InnerText,
HclHeredoc h => h.Content,
_ => null
};
}
```

### 2.3. Identification and comparison of transactions

File: `src/TerraformApi.Domain/Models/Sync/OperationFingerprint.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

/// Composite "fingerprint" of the operation for matching.
/// No field is required - those that are filled in are used
/// in comparison (see OperationMatchStrategy.Keys).
public sealed record OperationFingerprint
{
public string? OperationId { get; init; }
public string? Method { get ; init ; } // normalized to UPPER
    public string? UrlTemplate { get; init; } // normalized (see rules below)
public string? ParameterSignature { get; init; }
public string? Tag { get; init; }
public string? ApiName { get; init; } // for disambiguation between APIs in the same group

    /// What it was built from (for debugging/reporting).
    public OperationFingerprintSource SourceMarker { get; init; }
}

public enum OperationFingerprintSource
{
OpenApi,
ExistingTerraform,
Resolved // after applying TerraformInterpolationResolver
}

/// Which fingerprint fields can actually be compared and in what priority.
public enum OperationMatchKey
{
OperationId,
MethodAndUrl,
MethodAndUrlAndParams,
Tag,
ApiAndMethodAndUrl,
Custom
}

/// Strategy is an ordered list of keys.
/// The comparison goes from top to bottom; the first matching key → match .
public sealed record OperationMatchStrategy
{
/// Matching order. Default is the safest scheme for merging between environments.
    public IReadOnlyList<OperationMatchKey> Keys { get; init; } =
[
OperationMatchKey.MethodAndUrl,
OperationMatchKey.OperationId,
OperationMatchKey.Tag
];

/// Normalize URLs before comparison.
public UrlNormalizationOptions UrlNormalization { get; init; } = new();

/// Custom matcher for OperationMatchKey.Custom.
public Func<OperationFingerprint, OperationFingerprint, bool>? CustomMatcher { get; init; }

/// If enabled and comparison in structural-mode did not yield a match,
/// apply TerraformInterpolationResolver and try again.
public bool TryResolvedComparisonAsFallback { get; init; } = true;

/// Context of variables for resolution (if TryResolvedComparisonAsFallback = true
/// or strategy explicitly requires resolved-mode).
public IReadOnlyDictionary<string, string>? VariableContext { get; init; }
}

public sealed record UrlNormalizationOptions
{
public bool LowercaseScheme { get; init; } = true;
public bool TrimTrailingSlash { get; init; } = true;
public bool CollapseSlashes { get; init; } = true;
public bool NormalizeBraceParams { get; init; } = true; // {id} ≡ {ID} ≡ :id

    /// Whether to treat ` users` and `/ users` as the same.
    public bool TreatLeadingSlashAsOptional { get; init; } = true;
}
```

**URL normalization rules** (`UrlNormalizationOptions`):

| Option | Effect |
|---|---|
| `LowercaseScheme` | `HTTPS://...` → `https://...` (unlikely to appear in template, but just in case) |
| `TrimTrailingSlash` | `/users/` → `/users` |
| `CollapseSlashes` | `/users//{id}` → `/users/{id}` |
| `NormalizeBraceParams` | `{userId}` → `{param}` (for matching only, not for writing!). Optional, default - DOES NOT normalize names, only unifies the syntax `{ x }` vs `: x ` |
| `TreatLeadingSlashAsOptional` | `users` ≡ `/users` |

### 2.4. Merge Policy

File: `src/TerraformApi.Domain/Models/Sync/MergePolicy.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

public enum FieldMergePolicy
{
/// Never touch the existing value.
    Preserve ,

/// We write only if the field is missing or empty
/// ( null / "" / empty array / empty object).
    EnrichIfMissing ,

/// Overwrite unconditionally (used only in Convert scripts,
/// in Sync -script is prohibited for all fields by default).
    Overwrite
}

public enum CollectionMergePolicy
{
/// Never change the collection.
Preserve ,

/// Add elements from OpenAPI that are not in Terraform
/// (by item - fingerprint ). Existing ones remain untouched.
    AppendMissing ,

/// AppendMissing + if an element with the same fingerprint exists,
/// recursively apply enrichment to its fields.
    AppendAndEnrich ,

/// Complete replacement (prohibited in Sync ).
    Replace
}

public sealed record MergePolicy
{
/// What to do with the entire operation if it is not in OpenAPI , but is in TF .
    /// Append-only ⇒ Preserve.
public OperationPreservationMode UnknownOperationPolicy { get; init; }
        = OperationPreservationMode . Preserve ;

/// What to do with an operation that is in OpenAPI but not in TF .
    public NewOperationMode NewOperationPolicy { get; init; }
        = NewOperationMode . Append ;

/// Per - field policy for existing operations.
    /// Key is the name of the APIM operation field (operation_id, display_name, method,
/// url_template, status_code, description). Value — policy.
public IReadOnlyDictionary<string, FieldMergePolicy> OperationFieldPolicies
{ get; init; } = DefaultAppendOnlyFieldPolicies;

/// Policy for collections within an operation: request.header[],
/// request.query[], request.template[], responses[], etc.
public IReadOnlyDictionary<string, CollectionMergePolicy> CollectionPolicies
{ get; init; } = DefaultAppendOnlyCollectionPolicies;

/// Per-field policy for the API block (display_name, service_url, policy, etc.).
public IReadOnlyDictionary<string, FieldMergePolicy> ApiFieldPolicies
{ get; init; } = DefaultAppendOnlyApiFieldPolicies;

public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
DefaultAppendOnlyFieldPolicies = new Dictionary<string, FieldMergePolicy>
{
["operation_id"] = FieldMergePolicy.Preserve, // identity, do not touch
["method"] = FieldMergePolicy.Preserve, // do not change the method type
["url_template"] = FieldMergePolicy.Preserve, // do not change the URL
["display_name"] = FieldMergePolicy.EnrichIfMissing,
["description"] = FieldMergePolicy.EnrichIfMissing,
["status_code"] = FieldMergePolicy.EnrichIfMissing
};

public static readonly IReadOnlyDictionary<string, CollectionMergePolicy>
DefaultAppendOnlyCollectionPolicies = new Dictionary<string, CollectionMergePolicy>
{
["request.header"] = CollectionMergePolicy.AppendMissing,
["request.query"] = CollectionMergePolicy.AppendMissing,
["request.template"] = CollectionMergePolicy.AppendMissing,
["responses"] = CollectionMergePolicy.AppendMissing,
["responses.header"] = CollectionMergePolicy.AppendMissing,
["responses.representation"] = CollectionMergePolicy.AppendMissing
};

public static readonly IReadOnlyDictionary<string, FieldMergePolicy>
DefaultAppendOnlyApiFieldPolicies = new Dictionary<string, FieldMergePolicy>
{
["name"] = FieldMergePolicy.Preserve,
["display_name"] = FieldMergePolicy.Preserve,
["path"] = FieldMergePolicy.Preserve,
["service_url"] = FieldMergePolicy.Preserve,
["policy"] = FieldMergePolicy.Preserve,
["protocols"] = FieldMergePolicy.Preserve,
["revision"] = FieldMergePolicy.Preserve
};
}

public enum OperationPreservationMode
{
/// Append - only default: leave as is.
    Preserve,
/// Mark `deprecated` in the description (do not delete!).
MarkDeprecated,
/// Delete (for Convert from scratch).
Remove
}

public enum NewOperationMode
{
/// Add to the end of the api_operations array.
Append ,
/// We don’t add, we only report.
    ReportOnly ,
/// Add to a special place (for example, before a comment marker).
    AppendBeforeMarker
}
```

**Note for LLM **: The default policy is ** append - only semantics **. If the user wants to change ` display_name` during sync , they pass ` MergePolicy.WithOverride (" display_name " , FieldMergePolicy.EnrichIfMissing → Overwrite )` . This granularity is the requested "flexibility " .

### 2.5. Diffs and report

File: ` src / TerraformApi . Domain / Models / Sync / SyncReport . cs` .

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record OperationDiff
{
public required OperationFingerprint TerraformFingerprint { get; init; }
public OperationFingerprint? OpenApiFingerprint { get; init; }
public required OperationDiffKind Kind { get; init; }
public List<FieldDiff> FieldDiffs { get; init; } = [];
    /// What **was actually applied** (after passing through MergePolicy ).
    public List<string> AppliedChanges { get; init; } = [];
public List<string> SkippedDueToPolicy { get; init; } = [];
}

public enum OperationDiffKind
{
/// In both, there is no difference.
Identical ,
/// In both, there is a difference (see FieldDiffs ).
    Changed ,
/// Only in OpenAPI → will be added.
    AddedFromOpenApi ,
/// Only in Terraform → saved as is ( append - only ).
    PreservedFromTerraform ,
/// Marked as duplicate (see DuplicateGroups).
Duplicate
}

public sealed record FieldDiff
{
public required string FieldPath { get; init; } // "operation_id", "request.header[name=Auth]"
public required string? TerraformValue { get; init; }
public required string? OpenApiValue { get; init; }
public required FieldDiffOutcome Outcome { get; init; }
}

public enum FieldDiffOutcome
{
NoChange,
AppliedEnrichIfMissing,
AppliedOverwrite,
SkippedPreserve,
AppliedCollectionAppend
}

public sealed record DuplicateGroup
{
public required OperationMatchKey MatchedBy { get; init; }
public required string MatchedValue { get; init; } // eg "GET /users"
public List<DuplicateMember> Members { get; init; } = [];
}

public sealed record DuplicateMember
{
public required string OperationId { get; init; } // as in HCL, maybe with ${...}
public required string ApiGroupName { get; init; }
public required string ApiName { get; init; }
public required int LineInSource { get; init; }
public DuplicateSeverity Severity { get; init; }
}

public enum DuplicateSeverity
{
/// The same operation_id within one api_group/api is critical.
HardDuplicate,
/// Different operation_id, but the same (method, url) in one API - APIM will reject.
LogicalDuplicate ,
/// The same ( method , url ) in different api → acceptable, but suspicious.
    CrossApiSimilarity
}

public sealed record SyncReport
{
public required DateTime GeneratedAt { get; init; }
public required string ApiGroupName { get; init; }

public int TotalOperationsInTerraform { get; init; }
public int TotalOperationsInOpenApi { get; init; }

public int OperationsAdded { get; init; }
public int OperationsPreserved { get; init; }
public int OperationsEnriched { get; init; }
public int OperationsIdentical { get; init; }

public List<OperationDiff> Diffs { get; init; } = [];
public List<DuplicateGroup> Duplicates { get; init; } = [];

    /// Warnings that do not block sync but require review.
    public List<SyncWarning> Warnings { get; init; } = [];
}

public sealed record SyncWarning
{
public required string Message { get; init; }
public string? OperationId { get; init; }
public SyncWarningKind Kind { get; init; }
}

public enum SyncWarningKind
{
OperationIdContainsInterpolation, // operation_id = "${...}-${env}" — ok, but matching by structural
UrlTemplateContainsInterpolation,
AmbiguousMatch, // found multiple candidates
SkippedFieldDueToPolicy,
UnknownFieldInOpenApi,
DuplicateDetected
}

public sealed record SyncResult
{
public required bool Success { get; init; }
public required string TerraformConfig { get; init; } // final HCL
public required SyncReport Report { get; init; }
public OperationExecutionGraph? ExecutionGraph { get; init; }
public List<string> Errors { get; init; } = [];
}
```

---

## 3. Interfaces

### 3.1. `IHclParser`

File: `src/TerraformApi.Domain/Interfaces/IHclParser.cs`.

```csharp
public interface IHclParser
{
/// Parses HCL source into AST.
    /// Throws HclParseException with Line / Column specified on syntax errors.
    HclDocument Parse(string source);

/// Best-effort parsing: does not throw exceptions, returns ParseDiagnostic[].
HclParseResult TryParse(string source);
}

public sealed record HclParseResult
{
public HclDocument? Document { get; init; }
public List<HclParseDiagnostic> Diagnostics { get; init; } = [];
public bool IsSuccess => Document is not null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
```

### 3.2. `IHclWriter`

```csharp
public interface IHclWriter
{
string Write(HclDocument document, HclWriteOptions? options = null);
}

public sealed record HclWriteOptions
{
public int IndentSize { get; init; } = 2;
public bool AlignAssignmentEquals { get; init; } = true; // as in the example: long keys in one column
public int MaxAlignedKeyLength { get; init; } = 36;
public string LineEnding { get; init; } = "\n";
public bool PreserveOriginalFormatting { get; init; } = true; // reuses original heredocs/strings
}
```

### 3.3. `IApimTerraformReader`

```csharp
public interface IApimTerraformReader
{
ParsedApimDocument Read(string terraformSource);
ParsedApimDocument Read(HclDocument document);

    /// Possible structural patterns that the reader understands.
/// Each is the path from the root to the parent `< api _ group _ name >` blocks.
    IReadOnlyList<IReadOnlyList<string>> KnownApiGroupPaths { get; }
}
```

**Note**: reader tries paths in ` KnownApiGroupPaths ` order . Defaults:
1. `["apis", "bpc_apis", "backend_apis"]` (as in the example)
2. `["apis", "backend_apis"]`
3. `[]` (flat: ` api_group ={...}` immediately at the top level )

An additional path can be passed via ` ApimReaderOptions . CustomPaths` .

### 3.4. `IApimTerraformWriter`

```csharp
public interface IApimTerraformWriter
{
/// Writes after AST modifications . Inherits options from HclWriter .
    string Write(ParsedApimDocument parsed, HclWriteOptions? options = null);

    /// Helper: Build a ParsedApimDocument from scratch from ApimConfiguration
    /// (used in Convert script).
ParsedApimDocument BuildFromConfiguration(
ApimConfiguration configuration,
IReadOnlyList<string>? apiGroupParentPath = null);
}
```

### 3.5. `IOperationMatcher`

```csharp
public interface IOperationMatcher
{
/// Creates a fingerprint from ApimApiOperation (OpenAPI side).
OperationFingerprint FingerprintFromOpenApi(
ApimApiOperation operation,
OperationMatchStrategy strategy);

/// Creates a fingerprint from ParsedApiOperation (Terraform side).
OperationFingerprint FingerprintFromTerraform(
ParsedApiOperation operation,
OperationMatchStrategy strategy);

    /// Matches sets. Returns three partitions + a list of ambiguities.
    MatchResult Match(
IReadOnlyList<OperationFingerprint> openApiFingerprints,
IReadOnlyList<OperationFingerprint> terraformFingerprints,
OperationMatchStrategy strategy);
}

public sealed record MatchResult
{
/// Operations from OpenAPI that have no corresponding Terraform operations.
public List<OperationFingerprint> OnlyInOpenApi { get; init; } = [];

    /// Terraform operations that have no corresponding OpenAPI pair .
    public List<OperationFingerprint> OnlyInTerraform { get; init; } = [];

/// Pairs "Terraform ↔ OpenAPI". Left side is TF, right side is OpenAPI.
public List<(OperationFingerprint Tf, OperationFingerprint OpenApi)> Matched { get; init; } = [];

    /// Cases when one TF fingerprint matched several OpenAPIs or vice versa.
    public List<AmbiguousMatch> Ambiguities { get; init; } = [];
}

public sealed record AmbiguousMatch
{
public required OperationFingerprint Source { get; init; }
public required IReadOnlyList<OperationFingerprint> Candidates { get; init; }
public required OperationMatchKey AmbiguousOnKey { get; init; }
}
```

### 3.6. `IDuplicateDetector`

```csharp
public interface IDuplicateDetector
{
/// Runs all matching keys on one set (inside HCL ).
/// Returns groups where >1 operations have the same key.
    List<DuplicateGroup> Detect(
ParsedApimDocument parsed,
OperationMatchStrategy strategy);
}
```

### 3.7. `IAppendOnlySynchronizer`

```csharp
public interface IAppendOnlySynchronizer
{
SyncResult Synchronize(
ParsedApimDocument existingParsed,
ApimConfiguration newConfiguration,
MergePolicy policy,
OperationMatchStrategy matchStrategy);
}
```

---

## 4. Algorithms (pseudocode)

### 4.1. HCL Lexer

**Purpose**: Breaks the source into tokens.

**Token Types**:
```
LBRACE {
LBRACE } (same type, but K ind = Close )
LBRACKET [
[BRACKET]
EQUALS =
COMMA ,
IDENT [a-zA-Z_][a-zA-Z0-9_-]*
STRING "..." (with support for \" and ${...})
NUMBER \ d +(\.\ d +)?
HEREDOC_START << or <<-, then IDENT
HEREDOC _ END  IDENT at the beginning of the line, matching start
NEWLINE \ n (only significant within a heredoc and for a column tracking )
COMMENT # ... \n or // ... \n or /* ... */
EOF
```

**Pseudocode**:
```
read source char-by-char
maintain line, column

loop:
skip whitespace/comments (except newlines inside heredoc)
ch = peek()
case ch:
'{', '}' → emit LBRACE/RBRACE, advance
'[', ']' → emit LBRACKET/RBRACKET, advance
'=' → emit EQUALS, advance
',' → emit COMMA, advance
'"' → read_string() (see below)
'<' + '<' → read_heredoc_start_then_body()
digit → read_number()
letter or '_' → read_ident()
other → ParseError

read_string():
advance past "
buffer = ""
while peek() != '"':
if peek() == '\\':
advance, append next char as escape
elif peek() == '$' and peek_next() == '{':
      # this is part of the interpolation; read to the pair }
      read up to '}' including nested { }
    else:
append peek(), advance
advance past "
emit STRING(buffer, hadInterpolation: bool)

read_heredoc_body(marker):
# after <<MARKER\n comes content up to a line exactly equal to MARKER (or after <<-, with trim leading whitespace)
collect lines until a line that, trimmed, equals marker
emit HEREDOC(marker, content, indented)
```

** Edge cases **:
- `${...}` within a string: The string parser must recognize that the string contains interpolation. A single STRING token with the ` HasInterpolation = true` flag . The precise separation into "literal chunks" and "expressions" is done when creating ` HclInterpolation` .
- Nested `{` `}` inside `${...}` - parentheses need to be balanced.
- Heredoc indented `<<- XML `: Trim the minimum indentation of all lines when reading.
- The comments `#` and `//` are equivalent. Multi-line `/* */` are for completeness.

### 4.2. HCL Parser

**Grammar (simplified)**:
```
Document := Assignment*
Assignment := Key '=' Value
Key := IDENT | STRING_LITERAL_QUOTED
Value := Object | Array | Literal | Interpolation | Heredoc
Object := '{' Assignment* '}'
Array := '[' (Value (',' Value)* ','?)? ']'
Literal := NUMBER | BOOL | NULL | STRING_NO_INTERP
Interpolation := STRING_WITH_INTERP
Heredoc := HEREDOC token
```

**Recursive descent**, without backtracking. Each error throws `HclParseException(line, col, expected, found)`.

**Storing positions**: Each AST node stores the Line / Column of its first token.

### 4.3. ApimTerraformReader

**Algorithm**:
```
1. Parse(source) → HclDocument ast
2. For each known path :
   a . Navigate from the root along path (find HclObject )
   b . If found, we iterate over its HclAssignment s —each key is api_group_name
   c . For each api_group : look for HclAssignment " api " ( array) and " api_operations " ( array)
3. If no one is known path didn't work, trying path = [] (flat root):
We are looking for top - level HclAssignment s whose value is an HclObject with fields api / api_operations .
4. For each operation found ( HclObject ):
   a. Extract the required fields operation_id, method, and url_template
b . Extract optional fields
   c . Save the reference to HclObject (this is the future modification point)
5. Return ParsedApimDocument with the filled ApiGroups collection .
```

**Protection against false positives**: inside policy Heredoc (`<< XML ... XML `) encounters XML with `< method > GET </ method >` tags. This should not be included in the extracted operations, because a heredoc at the AST level is a single ` HclHeredoc` node, not an object with keys. Reader works only with ` HclObject` . This automatically solves the problem where the regex approach currently fails.

### 4.4. OperationMatcher

**Match Algorithm**:
```
function Match(openApiList, tfList, strategy):
matched = []
ambiguities = []

for each key in strategy.Keys:
        # build a reverse index on this key for the remaining TFs
        tfIndex = group_by(remaining_tf, key)
        # for each OpenAPI fingerprint matching
        for op in remaining_openapi:
candidates = tfIndex[op.key_value(key)]
if len(candidates) == 1:
matched.append((candidates[0], op))
remove candidates[0] from remaining_tf
remove op from remaining_openapi
elif len(candidates) > 1:
ambiguities.append(AmbiguousMatch(op, candidates, key))
else :
                pass # try the next key

        if remaining_openapi empty: break

if strategy.TryResolvedComparisonAsFallback and remaining_openapi non-empty:
        # Rebuild fingerprints with resolver and repeat the cycle
        resolved_openapi = [resolve(o) for o in remaining_openapi]
resolved_tf = [resolve(t) for t in remaining_tf]
# ... the same cycle

onlyInOpenApi = remaining_openapi
onlyInTerraform = remaining_tf
return MatchResult(matched, onlyInOpenApi, onlyInTerraform, ambiguities)
```

**Extracting key values from fingerprint 'a**:
```
key_value(fingerprint, key):
case key:
OperationId → fingerprint.OperationId
MethodAndUrl → fingerprint.Method + "|" + Normalize(fingerprint.UrlTemplate)
MethodAndUrlAndParams → ... + "|" + fingerprint.ParameterSignature
Tag → fingerprint.Tag
ApiAndMethodAndUrl → fingerprint.ApiName + "|" + Method + "|" + Url
Custom → strategy.CustomMatcher(...)
return null if any required field is null
```

**Constructing a ParameterSignature** from ApimApiOperation:
```
function ParameterSignature(operation):
parts = []
for request in operation.Requests:
for header in request.Headers:
parts.append("h:" + header.Name)
for query in request.QueryParameters:
parts.append("q:" + query.Name)
for template in request.TemplateParameters:
parts.append("t:" + template.Name)
parts.sort()
return "|".join(parts)
```
We compare parameter names using case - insensitive . The type ( string / int ) is not included in the signature by default (optional via `MatchStrategy.IncludeParameterTypesInSignature`).

### 4.5. DuplicateDetector

```
function Detect(parsed, strategy):
groups = []
for each key in [OperationId, MethodAndUrl, ApiAndMethodAndUrl]:
index = group_by(allOperations, key)
for value, members in index:
if len(members) > 1:
severity = determine_severity(key, members)
groups.append(DuplicateGroup(key, value, members, severity))
    # Also: for MethodAndUrlAndParams (if the parameters match completely - the strictest duplicate)
    return groups

function determine_severity(key, members):
if key == OperationId and same_api_group(members):
return HardDuplicate
if key == MethodAndUrl and same_api(members):
return LogicalDuplicate
if key == MethodAndUrl and different_api(members):
return CrossApiSimilarity
return info
```

### 4.6. AppendOnlySynchronizer (main algorithm)

```
function Synchronize(existingParsed, newConfig, policy, strategy):
report = new SyncReport
duplicates = duplicateDetector.Detect(existingParsed, strategy)
report.Duplicates = duplicates

# Find ApiGroup in existingParsed by name from newConfig.
    # If not, create a new one (this is the first sync for this group).
    targetGroup = existingParsed.ApiGroups.find(g => g.ApiGroupName matches newConfig.ApiGroupName)
if targetGroup is null:
targetGroup = appendNewApiGroup(existingParsed, newConfig)
        # all operations will go to Added

    tfFingerprints = targetGroup.Operations.Select(o => matcher.FingerprintFromTerraform(o, strategy))
openApiFingerprints = newConfig.ApiOperations.Select(o => matcher.FingerprintFromOpenApi(o, strategy))

matchResult = matcher.Match(openApiFingerprints, tfFingerprints, strategy)

    # --- 1. Operations that are only in OpenAPI : adding ---
    if policy.NewOperationPolicy == Append:
for openApiOp in matchResult.OnlyInOpenApi:
newAstNode = buildHclObjectForOperation(openApiOp.SourceModel, naming context)
appendToArray(targetGroup.AstNode.Get("api_operations") as HclArray, newAstNode)
report.Diffs.Add(OperationDiff{ Kind=AddedFromOpenApi, ... })
report . OperationsAdded ++

# --- 2. Operations that are unique to Terraform : leave ---
    for tfOp in matchResult.OnlyInTerraform:
# By default, UnknownOperationPolicy=Preserve — we do nothing with the AST.
# We just record it in the report.
report.Diffs.Add(OperationDiff{ Kind=PreservedFromTerraform, ... })
report.OperationsPreserved++

# --- 3. Matched: enrichment by policy ---
for (tfFp, openApiFp) in matchResult.Matched:
tfOp = find_parsed_op(tfFp)
openApiOp = find_openapi_op(openApiFp)
diff = computeDiff(tfOp, openApiOp)

applied = []
skipped = []
for fieldDiff in diff.FieldDiffs:
policy_for_field = resolvePolicy(fieldDiff.FieldPath, policy)
case policy_for_field:
Preserve:
skipped.append(fieldDiff.FieldPath)
fieldDiff.Outcome = SkippedPreserve
EnrichIfMissing:
if isMissing(tfOp, fieldDiff.FieldPath):
writeIntoAst(tfOp.AstNode, fieldDiff.FieldPath, fieldDiff.OpenApiValue)
applied.append(fieldDiff.FieldPath)
fieldDiff.Outcome = AppliedEnrichIfMissing
else:
skipped.append(fieldDiff.FieldPath)
fieldDiff.Outcome = SkippedPreserve
Overwrite:
writeIntoAst(...)
applied.append(...)

# Collections (request.header, responses, ...)
for collectionPath, items in diff.CollectionDiffs:
collPolicy = resolveCollectionPolicy(collectionPath, policy)
case call policy:
Preserve: skip
AppendMissing:
for item in items.OnlyInOpenApi:
appendToArray(tfOp.AstNode.descendant(collectionPath), buildHclObject(item))
applied.append(collectionPath + " += " + item.Name)

diffKind = applied.Empty ? Identical: Changed
report.Diffs.Add(OperationDiff{ Kind=diffKind, FieldDiffs=diff.FieldDiffs,
AppliedChanges=applied, SkippedDueToPolicy=skipped })
if diffKind == Changed: report.OperationsEnriched++
else: report.OperationsIdentical++

# Final HCL
finalHcl = writer.Write(existingParsed.Ast, options)
return SyncResult(true, finalHcl, report, executionGraph, errors=[])
```

### 4.7. HclWriter

**Principles**:
1. **We don't lose formatting** of existing nodes if they haven't been touched (which makes up 95% of the document). This is achieved by having unchanged nodes store the ` OriginalSource` ( a slice of the source by Line / Column ), and the writer inserts exactly that line.
2. **New nodes** are generated canonically: 2 spaces of indentation, alignment of `=` by the longest key (as in the example ` apim _ resource _ group _ name = "..."`).
3. ** Heredoc ' and** are written back as is (`<< XML ... XML `).
4. **Interpolations** are wrapped in quotes: `"${ api _ name }-${ env }"`.

**Pseudocode**:
```
function Write(document, options):
sb = StringBuilder
for assignment in document.RootAssignments:
writeAssignment(sb, assignment, indent=0)
return sb.toString()

function writeAssignment(sb, a, indent):
if a.OriginalSource and a not modified: # fast path
sb.append(a.OriginalSource); return
keyText = a.KeyIsQuoted ? "\"" + a.Key + "\"" : a.Key
sb.append(spaces(indent) + keyText + " = ")
writeValue(sb, a.Value, indent)
sb.append("\n")

function writeValue(sb, v, indent):
case v:
HclLiteral(String, raw): sb.append("\"" + escape(raw) + "\"")
HclLiteral(Other, raw): sb.append(raw)
HclInterpolation(text): sb.append("\"" + text + "\"")
HclHeredoc(marker, body, indented):
sb.append(indented ? "<<-" : "<<")
sb.append(marker + "\n" + body + "\n" + marker)
HclObject(assignments):
sb.append("{\n")
keyWidth = maxKeyLength(assignments)
for a in assignments:
sb.append(spaces(indent+2))
sb.append(padRight(a.Key, keyWidth) + " = ")
writeValue(sb, a.Value, indent+2)
sb.append("\n")
sb.append(spaces(indent) + "}")
HclArray(items):
sb.append("[\n")
for i, item in enumerate(items):
sb.append(spaces(indent+2))
writeValue(sb, item, indent+2)
if i < len(items) - 1: sb.append(",")
sb.append("\n")
sb.append(spaces(indent) + "]")
```

### 4.8. TerraformInterpolationResolver

```csharp
public sealed class TerraformInterpolationResolver
{
public string Resolve(string template, IReadOnlyDictionary<string, string> variables)
{
// template = "${api_name}-${env}"
// variables = { api_name: "bpc", env: "dev" }
        // result = " bpc - dev "

// Implementation: regex \$\{([^}]+)\}, for each group:
// - if variables contains a key → we substitute
// - otherwise we leave ${...} as is
// Return the result with a warning if there are any unresolved ${...}.
    }

public ResolveResult ResolveWithReport(string template, IReadOnlyDictionary<string, string> variables);
}

public sealed record ResolveResult
{
public required string Value { get; init; }
public List<string> UnresolvedExpressions { get; init; } = [];
public bool HasUnresolvedExpressions => UnresolvedExpressions.Any();
}
```

**Important**: The resolver only processes simple `${ var _ name }` and `${ var . path }`. Complex Terraform expressions (functions, ternaries) are **not** supported - they are left as is and reported in ` UnresolvedExpressions `.

---

## 5. Edge cases — a mandatory checklist for tests

The LLM implementing this **must write a test for each point below**, otherwise the feature is not considered complete.

### 5.1. HCL Parser

| # | Case | Expectation |
|---|---|---|
| P 1 | Empty document | ` HclDocument ` with empty ` RootAssignments `, no errors |
| P 2 | Comments only | Ditto |
| P 3 | Simple assignment ` a = " b "` | One assignment with ` HclLiteral { String ," b "}` |
| P4 | `a = "${x}"` | `HclInterpolation{InnerText="${x}"}`, `ReferencedExpressions=["x"]` |
| P5 | `a = "prefix-${x}-suffix"` | Same type; `InnerText` preserves everything; `ReferencedExpressions=["x"]` |
| P6 | `a = "${x}-${y}"` | `ReferencedExpressions=["x","y"]` |
| P7 | Heredoc `a = <<XML\n<foo/>\nXML` | `HclHeredoc{marker="XML", content="<foo/>", indented=false}` |
| P 8 | Indented heredoc `<<- XML ` with tabs/spaces at the beginning | Minimum indentation is trimmed, just like in TF spec |
| P 9 | Array of objects with trailing comma | Parses correctly |
| P 10 | Deeply nested structure from user example (5 levels) | Correct AST , ApiGroupParentPath = `[" apis " , " bpc_apis " ," backend_apis " ]` |
| P 11 | Key in quotes: `"${ api _ group _ name }" = { ... }` | `HclAssignment{Key="${api_group_name}", KeyIsQuoted=true}` |
| P12 | Invalid HCL (unbalanced parentheses) | `HclParseException` with valid Line/Column |
| P13 | Numbers: `port = 8080`, `ratio = 0.5` | `HclLiteral{Number}` |
| P14 | Bool/null: `flag = true`, `value = null` | Valid types |
| P 15 | Escaped string: ` a = " say \" hi \""` | Valid raw value |
| P 16 | XML inside a heredoc containing `=` and `{` `}` | Should not be interpreted as HCL |
| P 17 | Multi-byte UTF -8 characters | Works correctly |

### 5.2. Writer (round-trip)

| # | Case | Expectation |
|---|---|---|
| W 1 | Parse user example → write back → parse again | Two ASTs are structurally equal |
| W 2 | Heredoc is stored byte-for-byte | Contents of heredoc without modifications |
| W 3 | Interpolations are preserved | `"${ a }-${ b }"` remains `"${ a }-${ b }"` |
| W 4 | Alignment `=` for long keys in new blocks | ` apim _ resource _ group _ name = ...` - spaces as in example |
| W 5 | Trailing comma in arrays is saved/added | Agreed with options |

### 5.3. Reader ( APIM structure recognition )

| # | Case | Expectation |
|---|---|---|
| R 1 | Flat root: ` my - api = { api =[...], api _ operations =[...] }` | One ApiGroup |
| R 2 | Structure ` apis . bpc _ apis . backend _ apis . "${ api _ group _ name }"` | ApiGroupParentPath set |
| R 3 | Multiple ` api_group` under one parent | All recognized |
| R 4 | ` api _ operations ` is missing (only ` api `) | ApiGroup exists, Operations is empty |
| R 5 | ` api ` is missing (only ` api _ operations `) | ApiGroup is present, Apis is empty |
| R 6 | A `policy` field with a heredoc containing `<method> POST </method> ` | `<method> ` from XML should NOT go into Operations |

### 5.4. Matcher

| # | Case | Expectation |
|---|---|---|
| M 1 | OpenAPI op ` GET / users `, TF op ` GET / users ` (same operationId ) | Matched |
| M2 | OpenAPI `GET /users` (operationId=`listUsers`), TF `GET /users` (operationId=`${prefix}-list-${env}`) - match by MethodAndUrl | Matched |
| M3 | OpenAPI `GET /users`, TF `GET /Users` (different cases) | Options `NormalizeBraceParams=false`, `LowercaseScheme=true` - not a match (case in path matters), but with a special setting - a match |
| M4 | OpenAPI `GET /users/{id}`, TF `GET /users/{userId}` | Without `NormalizeBraceParams` - no match; s - match |
| M5 | Two OpenAPI operations with the same `(method, url)`, different query parameters | If the strategy is `MethodAndUrlAndParams` - different fingerprints; otherwise - `Ambiguity` |
| M6 | TF `url_template = "${operation_path}"` (fully interpolated) | Structural-fingerprint = `${operation_path}`. A match with OpenAPI is only possible if: (a) OpenAPI also gives `${operation_path}` (unlikely), or (b) resolved-mode with substitution |
| M7 | OpenAPI 3 operations, TF 3 operations, all matched | 3 matched, 0 in OnlyInOpenApi/OnlyInTerraform |
| M8 | OpenAPI empty, TF with operations | All TF → OnlyInTerraform; SyncReport.OperationsPreserved == count |
| M9 | TF empty, OpenAPI with operations | All OpenAPI → OnlyInOpenApi; OperationsAdded == count |

### 5.5. DuplicateDetector

| # | Case | Expectation |
|---|---|---|
| D1 | Two operation_id "x" in one api_group | HardDuplicate |
| D2 | Two different operation_id, same `(method, url)` in one api | LogicalDuplicate |
| D3 | Two different operation_id, same `(method, url)` in different apis | CrossApiSimilarity |
| D4 | Unique operation_id and `(method, url)` | Duplicate group is empty |
| D5 | Duplicates containing interpolations (`${env}`) | Comparison in structural-mode → two `"${prefix}-list-${env}"` entries are duplicates |

### 5.6. AppendOnlySynchronizer

| # | Case | Expectation |
|---|---|---|
| S1 | TF with 5 operations + OpenAPI with 5 same → identical | 0 changes in HCL, SyncReport.OperationsIdentical=5 |
| S 2 | TF with 5, OpenAPI adds 2 new ones → Added =2 | HCL has 2 new entries at the end of ` api _ operations ` |
| S 3 | TF with 5, OpenAPI removed 2 → Preserved = 2 for removed | HCL unchanged; SyncReport . OperationsPreserved = 2 |
| S4 | TF op without `description`, OpenAPI gives description → EnrichIfMissing | TF now has `description = "..."` |
| S5 | TF op with description="X", OpenAPI with description="Y", policy=Preserve | TF does not change |
| S6 | TF op without `request` block, OpenAPI gives parameters → CollectionPolicy=AppendMissing | In TF, a block `request = [{ header = [...] }]` is created |
| S7 | TF op with `request.header[name=Authorization]`, OpenAPI adds `header[name=X-Trace]` → AppendMissing | A new header is added to TF, the existing one is not touched |
| S8 | TF op with `url_template="/users/{id}"`, OpenAPI with `url_template="/v2/users/{id}"`, policy=Preserve by default | TF is unchanged; SyncReport contains SkippedDueToPolicy `url_template` |
| S9 | Passed strict policy.WithOverride("url_template", Overwrite) | TF changes; SyncReport.AppliedChanges contains `url_template` |
| S 10 | Several ApiGroups in TF , OpenAPI works only with one → the rest are not touched | Other ApiGroups do not change byte-for-byte (checked via round - trip ) |
| S 11 | ApiGroup from OpenAPI is missing in TF → new ApiGroup is created | New node added under correct parent path |
| S 12 | OpenAPI brings duplicate by `( method , url )` to existing TF operation (but with different operationId ) → Ambiguity | SyncReport . Warnings contains AmbiguousMatch ; nothing is added |

### 5.7. Integration scenarios

| # | Scenario | What to check |
|---|---|---|
| I 1 | User Scenario 1 ( Convert from scratch ) — empty existingTerraform , OpenAPI with 10 ops | The output is valid HCL , all 10 operations, ExecutionGraph is present, Statistics are correct |
| I 2 | User Scenario 2 ( Sync ) - working user example + new OpenAPI with 1 new operation and 1 modified description | Output HCL = original + 1 new operation in api_operations + description filled in for one existing operation; nothing deleted; round - trip of other blocks is identical |
| I 3 | Large real HCL (50+ operations, 5+ api_group ) + OpenAPI with intersecting subset | Performance < 1 sec; matching correctness |

---

## 6. Phased Implementation Plan (for Opus )

> **Each phase = separate PR **. After each - ` dotnet build ` and green tests.

### Phase 0 – Preparation (15 min)

- [ ] Create branch ` feature / apim - sync - engine `
- [ ] Commit baseline : ` dotnet test ` → all 274 tests are green
- [ ] Create empty folders:
- ` src / TerraformApi . Domain / Models / Hcl /`
  - `src/TerraformApi.Domain/Models/Sync/`
- `src/TerraformApi.Domain/Models/Apim/`
- `src/TerraformApi.Application/Services/Hcl/`
- `src/TerraformApi.Application/Services/Apim/`
- `src/TerraformApi.Application/Services/Sync/`
- `tests/TerraformApi.Application.Tests/Hcl/`
- `tests/TerraformApi.Application.Tests/Sync/`
- [ ] Put a working user example in `tests/TerraformApi.Application.Tests/Fixtures/example-existing.tf` (as is, without modifications)

**Acceptance**: the branch is ready, the fixture is there, the tests are green.

### Phase 1 - HCL AST + Parser + Writer

**Files**:
- `Domain/Models/Hcl/*.cs` (all AST models, see §2.1)
- `Domain/Interfaces/IHclParser.cs`, `IHclWriter.cs`
- `Application/Services/Hcl/HclLexer.cs`
- `Application/Services/Hcl/HclParserService.cs` (implements `IHclParser`)
- `Application/Services/Hcl/HclWriterService.cs` (implements `IHclWriter`)
- `Application/DependencyInjection.cs` - registration

**Tests** (`tests/TerraformApi.Application.Tests/Hcl/`):
- `HclLexerTests.cs` — 15+ tests (all tokens, edge cases with heredocs, interpolations, and escaping)
- `HclParserTests.cs` — all P1–P17 from §5.1
- `HclWriterTests.cs` — all W1–W5 from §5.2
- **`HclRoundTripTests.cs`** — critical test: parse `example-existing.tf` → write it back → parse again → ASTs are equal. This test blocks the merge phase.

** Acceptance **:
- All phase tests are green
- ` HclRoundTripTests . RoundTripPreservesExistingExample ` passes
- Regression tests from previous phases are green

### Phase 2 — Sync Domain Models

**Files**:
- `Domain/Models/Apim/ParsedApimDocument.cs`, `ParsedApiGroup.cs`, `ParsedApi.cs`, `ParsedApiOperation.cs`, `HclValueRef.cs`
- `Domain/Models/Sync/OperationFingerprint.cs`
- `Domain/Models/Sync/OperationMatchKey.cs` (enum)
- `Domain/Models/Sync/OperationMatchStrategy.cs`
- `Domain/Models/Sync/UrlNormalizationOptions.cs`
- `Domain/Models/Sync/FieldMergePolicy.cs` (enum)
- `Domain/Models/Sync/CollectionMergePolicy.cs` (enum)
- `Domain/Models/Sync/MergePolicy.cs`
- `Domain/Models/Sync/OperationDiff.cs`, `FieldDiff.cs`
- `Domain/Models/Sync/DuplicateGroup.cs`
- `Domain/Models/Sync/SyncReport.cs`, `SyncResult.cs`, `SyncWarning.cs`

**There are no tests at this phase** (these are pure records ). But: compilation check, defaults check - a simple test for ` MergePolicy` . DefaultAppendOnlyFieldPolicies` ( that ` operation_id` , ` method` , ` url_template` = Preserve ) .

** Acceptance **: compilation, defaults are correct.

### Phase 3 - ApimTerraformReader + Writer

**Files**:
- `Domain/Interfaces/IApimTerraformReader.cs`, `IApimTerraformWriter.cs`
- `Application/Services/Apim/ApimTerraformReaderService.cs`
- `Application/Services/Apim/ApimTerraformWriterService.cs`
- `Application/DependencyInjection.cs` - registration

**Reader Algorithm** (see §4.3).

**Writer Algorithm**:
- `Write(parsed)` → simply delegates to `IHclWriter.Write(parsed.Ast, options)` (the writer works on the AST, which is already modified by the synchronizer).
- `BuildFromConfiguration(config, parentPath)` — constructs an AST from scratch:
- Creates a chain of `HclObject`s by `parentPath`
- At the bottom, it creates `HclAssignment{Key=apiGroupName, KeyIsQuoted=true if name has ${...}}`
- Value — `HclObject` with three assignments: `product=[]`, `api=[...]`, `api_operations=[...]`
- Each `ApimApiOperation` → `HclObject` with fields

**Tests** (`tests/TerraformApi.Application.Tests/Apim/`):
- R1–R6 from §5.3
- `BuildFromConfiguration_ProducesValidStructure`
- `BuildFromConfiguration_WithCustomParentPath_GeneratesNestedStructure`

**Acceptance**: reader correctly extracts operations from `example-existing.tf` (write a test that catches the **exact** number of operations), does not confuse `<method>` inside policy with the `method` field.

### Phase 4 - TerraformInterpolationResolver

**Files**:
- `Application/Services/Sync/TerraformInterpolationResolver.cs`
- `Domain/Models/Sync/ResolveResult.cs`

**Tests**:
- `Resolve_SimpleVariable_ReturnsValue` — `${env}` + `{env: dev}` = `dev`
- `Resolve_MultipleVariables` — `${a}-${b}` + `{a:1, b:2}` = `1-2`
- `Resolve_MissingVariable_LeftAsIs` — `${unknown}` + `{}` = `${unknown}`, `UnresolvedExpressions=["unknown"]`
- `Resolve_VarDotPath_Supported` — `${var.foo}` + `{var.foo: bar}` = `bar`
- `Resolve_NoInterpolation_PassThrough` — `"plain"` = `"plain"`
- `Resolve_ComplexExpression_LeftAsIs` - `${var.x ? "a" : "b"}` → unresolved

**Acceptance**: all tests are green.

###Phase 5 - OperationMatcher

**Files**:
- `Domain/Interfaces/IOperationMatcher.cs`
- `Application/Services/Sync/OperationMatcherService.cs`

**Tests**: M 1– M 9 from §5.4.

** Acceptance **: All tests are green. Special attention to M 6 (fully interpolated URL ) – the test should verify that without resolved mode, the operation remains in OnlyInTerraform , and with resolved mode , it matches.

### Phase 6 - DuplicateDetector

**Files**:
- `Domain/Interfaces/IDuplicateDetector.cs`
- `Application/Services/Sync/DuplicateDetectorService.cs`

**Tests**: D 1– D 5 from §5.5 + test on ` example - existing . tf ` (there are currently no duplicates, check that the detector returns an empty list).

### Phase 7 - AppendOnlySynchronizer

This is the biggest phase.

**Files**:
- `Domain/Interfaces/IAppendOnlySynchronizer.cs`
- `Application/Services/Sync/AppendOnlySynchronizerService.cs`

**Service structure**:
```csharp
public sealed class AppendOnlySynchronizerService : IAppendOnlySynchronizer
{
private readonly IOperationMatcher _matcher;
private readonly IDuplicateDetector _duplicateDetector;
private readonly IApimTerraformWriter _writer;
private readonly IApimTerraformReader _reader;
private readonly TerraformInterpolationResolver _resolver;
// logger

public SyncResult Synchronize(...)
{
// see §4.6
}

// Private methods:
private OperationDiff ComputeOperationDiff(ParsedApiOperation tf, ApimApiOperation openApi);
private void ApplyFieldEnrichment(HclObject astNode, string fieldPath, string newValue);
private void AppendOperationToArray(HclArray operationsArray, ApimApiOperation op, HclWriteContext ctx);
private HclObject BuildOperationHclObject(ApimApiOperation op);
private bool IsFieldMissing(HclObject astNode, string fieldPath);
private FieldMergePolicy ResolvePolicy(string fieldPath, MergePolicy policy);
private CollectionMergePolicy ResolveCollectionPolicy(string path, MergePolicy policy);
}
```

**Tests**: S 1– S 12 from §5.6. Also:
- `Synchronize_AppendOnlyDefaults_NeverModifiesPreserveFields`
- `Synchronize_WithCustomPolicy_AllowsOverwriteWhenSpecified`
- `Synchronize_RealUserExample_AddsNewOperationOnly` — take `example-existing.tf`, prepare OpenAPI with one new operation, do sync, check that: (a) the HCL is valid at the output, (b) it is parsed back, (c) there is exactly one new entry in `api_operations`, (d) all original entries are present byte-for-byte.

### Phase 8 — Integration with Conversion Orchestrator

**Files**:
- `Application/Services/ConversionOrchestratorService.cs` — add the `Sync()` method
- `Domain/Models/SyncRequest.cs` - DTO with openApiJson, existingTerraform, settings, MergePolicy, OperationMatchStrategy
- `Application/DependencyInjection.cs` — all registrations

**Changes in orchestrator**:
```csharp
public sealed class ConversionOrchestratorService : IConversionOrchestrator
{
// existing dependencies
private readonly IAppendOnlySynchronizer _synchronizer;
private readonly IApimTerraformReader _reader;
private readonly IOperationExecutionGraphBuilder _graphBuilder;

    // existing Convert () - no changes (or we want to include it too
// ExecutionGraph as in the original plan; this can be done as a separate PR )

    public SyncResult Sync(SyncRequest request)
{
var newConfig = _parser.Parse(request.OpenApiJson, request.Settings);
var parsed = string.IsNullOrEmpty(request.ExistingTerraform)
? new ParsedApimDocument { Ast = new HclDocument() } // empty
: _reader.Read(request.ExistingTerraform);

var syncResult = _synchronizer.Synchronize(
parsed,
newConfig,
request.MergePolicy?? new MergePolicy(),
request.MatchStrategy ?? new OperationMatchStrategy());

// Optional: build an ExecutionGraph on top of SyncReport
var graph = _graphBuilder.BuildFromSyncReport(syncResult.Report, newConfig.ApiGroupName);
return syncResult with { ExecutionGraph = graph };
}
}
```

**Old `Convert(json, settings, existingTerraform)`** delegates to `Sync()` for backward-compat.

**Tests** in `tests/TerraformApi.Application.Tests/Orchestrator/`:
- `Sync_FromScratch_GeneratesValidConfig`
- `Sync_WithExisting_PreservesExisting`
- `Sync_ProducesPopulatedSyncReport`

### Phase 9 - API endpoint + MCP tool

**Files**:
- `src/TerraformApi.Api/Endpoints/SyncEndpoint.cs` - `POST /api/sync`
- `src/TerraformApi.Mcp/Tools/SyncTool.cs` — MCP tool `sync_openapi_with_terraform`
- Update `update_terraform_from_openapi` - internally delegate to the new Sync with the default append-only policy for backward-compat

**API contract `POST /api/sync`**:
```json
{
"openApiJson": "...",
"existingTerraform": "...",
"environment": "dev",
"apiGroupName": "my-api-group",
"settings": { ... other settings ... },
"mergePolicy": {
"unknownOperationPolicy": "Preserve",
"newOperationPolicy": "Append",
"operationFieldOverrides": { "description": "Overwrite" }
},
"matchStrategy": {
"keys": ["MethodAndUrl", "OperationId"],
"urlNormalization": { "trimTrailingSlash": true, "treatLeadingSlashAsOptional": true }
},
"variableContext": { "env": "dev", "api_name": "bpc" }
}
```

**Response**: `SyncResult` serialized as JSON, with `terraformConfig` (string) and `report` (object) fields.

** Tests**: Add integration tests to ` tests / TerraformApi.Api.Tests / ` .

### Phase 10 — UI (optional, separate PR )

- In the existing web UI add " Sync " tab
- Fields: OpenAPI input, existing Terraform input, advanced policy editor
- Output: diff-view (new/unchanged/preserved/enriched), HCL download

---

## 7. What we DON'T do in this feature (moved to separate tasks)

| We don't do it now | When | Reason |
|---|---|---|
| Full HCL 2 parser with support for - expressions , ternaries | When needed | The current subset is sufficient for APIM config |
| Parsing `.tfvars` files | Separate task | Not needed for the synchronization itself; the variable context can be passed as a Dictionary |
| Merging with remote state ( terraform . tfstate ) | Separate task | This is a completely different model |
| Automatic CI with ` Terraform validate ` | Single task | Dependent on Terraform CLI ; add to infrastructure |
YAML support OpenAPI ( JSON only for now) | Can be added later | Doesn't block main flow |
| Mermaid / CSV export SyncReport | Phase 11 (after the main feature) | Already described in the existing Operation plan Execution Graph |

---

## 8. Definition of Done

LLM considers a feature complete when **all** items are met:

- [ ] All files from §1.1 are created and compiled
- [ ] All tests from §5 are written and green
- [ ] **Round-trip-test** on `example-existing.tf` green
- [ ] ** Append - only - test** on ` example - existing . tf ` + OpenAPI with 1 new operation: output source + 1 record, nothing deleted
- [ ] All 274 existing tests remain green (no regression in Convert / Validate / Transform / Fetch )
- [ ] ` POST / api / sync ` responds with 200 to a valid request
- [ ] MCP tool `sync_openapi_with_terraform` is available and working
- [ ] `ConversionOrchestratorService.Convert(json, settings, existingTerraform)` (old signature) delegates to the new Sync and does not break old tests
- [ ] Logging: every applied and skipped change is logged at the Information level
- [ ] Added " Append - only " section to ` README . md ` sync " with a sample request/response
- [ ] In ` docs / sync - policies . md ` (new file) - a table of all default policies with justifications

---

## 9. Test case for final acceptance

**Given**:
- ` existingTerraform ` = user's working example (complete, without modifications).
- ` openApiJson ` = OpenAPI 3.0 with:
- One operation that **already exists** in the existing TF (the same ` operationId` after resolving variables, the same method + url )
- One operation that is **missing** in TF (new)
- Without operations that are in TF but not in OpenAPI (that is, OpenAPI does not claim complete coverage)

**Request**: `POST /api/sync` with default `mergePolicy` (append-only) and `matchStrategy` = `[MethodAndUrl, OperationId]`.

**Expected result**:
1. The output HCL is **parsed** without errors.
2. HCL contains **exactly one new entry** in ` api_operations` (after the original ones ).
3. All other lines are identical to the source (check: ` originalHcl . Split ('\ n ')[0.. N ]` == ` resultHcl . Split ('\ n ')[0.. N ]` for all unchanged blocks).
4. `SyncReport`:
- `OperationsAdded = 1`
- `OperationsIdentical = 1`
- `OperationsPreserved = 0` (because OpenAPI didn't try to delete)
- `Duplicates = []`
- `Warnings` contains at least `OperationIdContainsInterpolation` for existing operations (because `operation_id = "${operation_prefix}-${env}"`)
5. `ExecutionGraph.Statistics.TotalOperations = 2`, `NewOperations = 1`, `IncludedOperations = 2`.

**This test is mandatory for final acceptance of the feature.**

---

## 10. Tips and Notes for Opus

1. Don't try to write everything at once. Go through the steps. After each one, run ` dotnet build ` and ` dotnet test `. If something is red, fix it before moving on.

2. ** HCL Lexer and Parser are the riskiest parts**. Write tests BEFORE implementation ( TDD ). Especially P4 , P5 , P10 , P11 .

3. ** AST round - trip is a parser correctness criterion**. If round - trip breaks ` example - existing . tf `, there's nothing else to do—fix this first.

4. **When you're unsure how to handle a rare case**, add ` HclParseDiagnostic` with a Warning level ; don't throw an exception. The parser should be tolerant.

5. **HclWriter must have `PreserveOriginalFormatting = true` by default**. This means: for each ` HclNode` , we store a slice of the source code positionally. If the node has not been modified, we output the slice as is. Only for new/modified nodes, we generate text from the template. This is the "minimal change guarantee" required by append - only .

6. **No regex for extracting operations**. If you catch yourself thinking, "It's easier to just use regex here ," stop: you're adding the technical debt that this whole feature is designed to eliminate. Reader works only through AST.

7. **OperationFingerprint — a record with value-equality**. Use records , not classes. This simplifies comparisons and indexing ( HashSet , Dictionary ).

8. **All public records are ` sealed `**. This is a project convention (see existing ` ApimApi `, ` ApimApiOperation `, etc.).

9. ** DI : Everything new is registered as ` Singleton` ** (no state between calls).

10. **Logging**: Every AST change is logged. Use the existing `ILogger<T>` pattern from the project. Example: `_logger.LogInformation("Enriched operation {OperationId}: field {FieldPath} set to {NewValue}", opId, field, value);`.

11. When adding ` Sync ()` to orchestrator , don't delete the existing ` Convert ()`. Make ` Convert ()` a wrapper so that old tests (173 in Application + 34 in Api + 67 in Mcp ) don't fail.

12. **Documentation in code**: every public The record / interface must have a `///` summary . This is not an option, it is a design requirement.

---

---

# REVISION 1 — Additions ( TemplateProfile , Style Detection , Comments , MCP​

This section **supplements** the main outline of §1–§9. If anything here contradicts the earlier text, REVISION 1 takes precedence.

## § REV -1. UX invariant that we want to ensure

The user should be able to do **one** of the following and get the correct result **without any additional configuration**:

OpenAPI only ** → get Terraform with templated values (`${ apim _ name }`, `${ env }`, etc.), plus a header comment with a list of placeholders to replace.
2. **Insert OpenAPI + existing Terraform file (of any kind)** → get the same file with new methods added, formatted in the same style as the existing ones (literal → literal, template → template), plus descriptive comments above each inserted operation.
3. **Insert only Terraform file** → get **analysis**: list of API groups `( apim _ resource _ group _ name , api _ name )`, list of operations in each group, detected placeholders, recommended ` ApimTemplateProfile `.

Each of these scenarios works **without specifying a mode** - the system determines it itself upon input.

---

## § REV -1.2. Placeholder Registry and ` ApimTemplateProfile`

### REV -1.2.1. Full registry (based on a working user example + recommended extensions)

| Layer | HCL Field | Placeholder by default | Category | Obligation |
|---|---|---|---|---|
| api | ` apim_resource_group_name` |​​​​​​`${ stage _ group _ name }` | Infrastructure | ** must templatize ** |
| api | `apim_name` | `${apim_name}` | Infrastructure | **must templatize** |
| api | `name` | `${api_name}-${env}` | Identity | **must templatize** |
| api | `display_name` | `${api_display_name} - ${env}` | Identity | **must templatize** |
| api | `path` | `${api_path_prefix}.${env}/v1/${api_path_suffix}` | Routing | **must templatize** |
| api | `service_url` | `https://${api_gateway_host}/${api_version}/${backend_service_path}/` | Routing | **must templatize** |
| api | `revision` | `${api_revision}` | Versioning | nice-to-have |
| api | `product_id` | `${product_id}` | Authorization | only when set |
| api | `subscription_key_parameter_names` | `${subscription_key_parameter_names}` | Authorization | only when set |
| api | `protocols` | `["https"]` (literal) | Security | DO NOT templatize |
| api | `soap_pass_through` | `false` (literal) | Capabilities | NOT templatized |
| api | `subscription_required` | `${subscription_required}` | Authorization | recommended |
| api_operation | `operation_id` | `${operation_prefix}-${env}` | Identity | **must templatize** |
| api_operation | `apim_resource_group_name` | `${stage_group_name}` | Infrastructure | **must templatize** |
| api_operation | `apim_name` | `${apim_name}` | Infrastructure | **must templatize** |
| api_operation | `api_name` | `${api_name}-${env}` | Identity | **must templatize** |
| api_operation | `display_name` | value from OpenAPI `summary` (literal) | Documentation | NOT templatized |
| api_operation | `method` | `GET` / `POST` / ... (literal) | Routing | **NOT ALLOWED** — APIM enum |
| api_operation | `url_template` | value from OpenAPI `path` (literal) | Routing | DO NOT templatize (this is the contract) |
| api_operation | `status_code` | `"200"` (literal) | Routing | DO NOT templatize |
| api_operation | `description` | from OpenAPI (literal) | Documentation | DO NOT templatize |
| CORS policy | `<origin>` URLs | `https://${frontend_host}.${env}.${company_domain}` + `https://${local_dev_host}:${local_dev_port}` | CORS | **must templatize** |

### REV -1.2.2. Additional placeholders (my recommendation - WHAT ELSE MAKES SENSE TO REMOVE)

Your current example does not have these variables, but I suggest adding them (optionally, via an advanced profile):

| Placeholder | Where it is used | Why |
|---|---|---|
| `${ env }` | everywhere, as a suffix/infix | You already have one, but it's worth fixing as "canonical" - it's the most common variable |
| `${ api _ version }` | in ` service _ url ` and optionally in ` path ` | You have it hardcoded in ` service _ url `, but it makes sense to have it in ` name `/` path ` for versioned APIs (` my - api - v 2- dev `) |
| `${ tenant _ id }` / `${ subscription _ id }` | in backend ARM - resource - id links in OAuth policy | When APIM references resources by ARM - ID ( key vault , identity , log analytics ) |
| `${ backend _ url _ protocol }` | in ` service _ url ` | For development you can use ` http `, for prod ` https `; removes hardcode ` https ://` |
| `${cors_allow_credentials}` | in CORS policy | `true` for prod with auth, `false` for public API |
| `${subscription_key_header_name}` | in `subscription_key_parameter_names` | Defaults to `Ocp-Apim-Subscription-Key`, but sometimes custom |
| `${rate_limit_calls_per_minute}` | in inbound rate-limit policy | Different limits on Wednesdays (dev permissive, prod strict) |
| `${oauth_authority_url}` | in `validate-jwt` policy | When OAuth verification is enabled; different AAD tenants by environment |
| `${oauth_audience}` | ibid. | Audience claim |
| `${log_analytics_workspace_id}` | in `log-to-eventhub` or Application Insights policy | Enabling logging |
| `${product_subscription_required}` | in product config | If a product is generated |
| `${product_approval_required}` | in product config | Same |
| `${api_revision_description}` | in the `revision_description` field | Not all modules have it, optional |

**All "additional" placeholders are disabled by default** - they are activated only if the corresponding feature is used (for example, `${ oauth _ authority _ url }` appears only if the policy contains `< validate - jwt >`).

### REV-1.2.3. Domain model `ApimTemplateProfile`

File: `src/TerraformApi.Domain/Models/Sync/ApimTemplateProfile.cs`.

```csharp
namespace TerraformApi.Domain.Models.Sync;

/// Templating profile: which HCL fields → which Terraform expressions.
public sealed record ApimTemplateProfile
{
public required string Name { get; init; }

    /// Mapping " api field name" → " HCL value " (as it will be displayed inside the quotes,
/// with ${...} interpolations).
/// If the field is not in the dictionary → the value is taken from the OpenAPI /settings literal.
    public IReadOnlyDictionary<string, string> ApiFieldTemplates { get; init; }
= new Dictionary<string, string>();

public IReadOnlyDictionary<string, string> OperationFieldTemplates { get; init; }
= new Dictionary<string, string>();

/// A template for operation_id that supports substitution.
    /// If it contains `{ op }` - it will be replaced with the operationId from OpenAPI (normalized).
/// Defaults to a common prefix without substitution: `${ operation _ prefix }-${ env }`.
/// Alternative: `${ operation _ prefix }-{ op }-${ env }` → each operation will get a unique template.
    public string? OperationIdTemplate { get; init; }

public CorsTemplateVariables CorsVariables { get; init; } = new();

    /// Whether to use literals for url_template and method ( true is recommended )
/// because this is the API contract ).
    public bool KeepRoutingFieldsLiteral { get; init; } = true;

/// Whether to templatize display_name (by default, we leave the literal from OpenAPI summary).
public bool TemplatizeDisplayName { get; init; } = false;

    /// ================ Ready-made profiles ================

/// Profile 1 - exactly matches the user's working example.
    public static readonly ApimTemplateProfile UserExampleProfile = new()
{
Name = "UserExampleProfile",
ApiFieldTemplates = new Dictionary<string, string>
{
["apim_resource_group_name"] = "${stage_group_name}",
["apim_name"] = "${apim_name}",
["name"] = "${api_name}-${env}",
["display_name"] = "${api_display_name} - ${env}",
["path"] = "${api_path_prefix}.${env}/v1/${api_path_suffix}",
["service_url"] = "https://${api_gateway_host}/${api_version}/${backend_service_path}/",
["revision"] = "${api_revision}",
["product_id"] = "${product_id}"
},
OperationFieldTemplates = new Dictionary<string, string>
{
["operation_id"] = "${operation_prefix}-${env}",
["apim_resource_group_name"] = "${stage_group_name}",
["apim_name"] = "${apim_name}",
["api_name"] = "${api_name}-${env}"
},
OperationIdTemplate = "${operation_prefix}-${env}"
};

/// Profile 2 - extended, with all "recommended" placeholders.
public static readonly ApimTemplateProfile ExtendedProfile = new()
{
Name = "ExtendedProfile",
ApiFieldTemplates = new Dictionary<string, string>
{
["apim_resource_group_name"] = "${stage_group_name}",
["apim_name"] = "${apim_name}",
["name"] = "${api_name}-${api_version}-${env}",
["display_name"] = "${api_display_name} - ${env}",
["path"] = "${api_path_prefix}.${env}/${api_version}/${api_path_suffix}",
["service_url"] = "${backend_url_protocol}://${api_gateway_host}/${api_version}/${backend_service_path}/",
["revision"] = "${api_revision}",
["product_id"] = "${product_id}",
["subscription_required"] = "${subscription_required}"
},
OperationFieldTemplates = new Dictionary<string, string>
{
["operation_id"] = "${operation_prefix}-{op}-${env}",
["apim_resource_group_name"] = "${stage_group_name}",
["apim_name"] = "${apim_name}",
["api_name"] = "${api_name}-${api_version}-${env}"
},
OperationIdTemplate = "${operation_prefix}-{op}-${env}"
    };

/// Profile 3 - without templates, everything is a literal (for one-time generation).
    public static readonly ApimTemplateProfile LiteralProfile = new()
{
Name = "LiteralProfile",
ApiFieldTemplates = new Dictionary<string, string>(),
OperationFieldTemplates = new Dictionary<string, string>(),
OperationIdTemplate = null,
KeepRoutingFieldsLiteral = true
};
}

public sealed record CorsTemplateVariables
{
public string FrontendHostExpr { get; init; } = "${frontend_host}";
public string EnvExpr { get; init; } = "${env}";
public string CompanyDomainExpr { get; init; } = "${company_domain}";
public string LocalDevHostExpr { get; init; } = "${local_dev_host}";
public string LocalDevPortExpr { get; init; } = "${local_dev_port}";
public string AllowCredentialsExpr { get; init; } = "true"; // or "${cors_allow_credentials}"
}
```

### REV -1.2.4. Algorithm for applying a profile during generation

When `TerraformGeneratorService.Generate(config, profile)` builds an AST for a new HCL:

```
for each field in api / api_operation :​
    template = profile.<Section>FieldTemplates.GetValueOrDefault(field)
if template != null:
# template - we put interpolation
node = HclInterpolation { InnerText = applyOpIdSubstitution(template, op?.OperationId) }
else :
# literal from OpenAPI or default
        node = HclLiteral { Kind=String, RawValue = literalValue }
write node into AST under field
```

Function `applyOpIdSubstitution(template, opId)`:
- If `template` contains `{op}` → replace it with the normalized `opId` (snake_case or kebab-case according to the config).
- Otherwise, return ` template ` unchanged.

**Normalization example**: OpenAPI operationId `listUserById` → kebab `list-user-by-id` → template `${operation_prefix}-list-user-by-id-${env}`.

### REV-1.2.5. `IApimTemplateProfileApplier`

```csharp
public interface IApimTemplateProfileApplier
{
/// Applies a profile to an existing AST : replaces the literals of the corresponding
/// fields for interpolation (if the profile templates them) AND/OR
/// adds missing fields.
///
/// IMPORTANT: This operation MAY change the values of literals, so it
/// NOT append - only . Used only in explicit conversion mode.
    /// "literal → templated" via the MCP tool apply_template_profile.
ParsedApimDocument Apply(
ParsedApimDocument document,
ApimTemplateProfile profile,
ApplyProfileOptions options );

/// Reverse operation: substitutes the values of variables into placeholders,
/// getting the "resolved" literal HCL for a specific environment.
    ParsedApimDocument Resolve(
ParsedApimDocument document,
IReadOnlyDictionary<string, string> variableValues);
}

public sealed record ApplyProfileOptions
{
/// Apply profile to fields that already have a value?
/// false ( default ) - only to empty/missing ones.
    public bool OverwriteExisting { get; init; }

    /// Add REPLACE BEFORE APPLY comments before modified blocks.
    public bool AddReplaceComments { get; init; } = true;
}
```

---

## §REV-1.3. Style Detection + Auto-Grouping

### REV -1.3.1. ` IApimTemplateProfileDetector`​

When a user inserts an existing file, we need to understand what style they're using and what placeholders they already have. Based on this, we build a **continuation** style when inserting new operations.

File: `src/TerraformApi.Domain/Interfaces/IApimTemplateProfileDetector.cs`.

```csharp
public interface IApimTemplateProfileDetector
{
/// Parses the existing HCL and returns the detected profile
/// + diagnostics + suggestions.
DetectedProfile Detect(ParsedApimDocument document);
}

public sealed record DetectedProfile
{
/// Profile built based on actual file data**.
/// It can be immediately used to generate new operations in the same style.
    public required ApimTemplateProfile InferredProfile { get; init; }

    /// Which fields were encountered as interpolations (with frequency indication).
    public List<DetectedField> DetectedFields { get; init; } = [];

    /// All ${...} names that have appeared at least once. This is the "global dictionary."
/// variables that the user will need to define in . tfvars .
    public HashSet<string> AllReferencedVariables { get; init; } = [];

    /// Found literal values (for example, " apim - company - dev " occurs
/// in ` apim_name` of all operations). Useful for autocomplete suggestions
/// when generating new ones.
    public IReadOnlyDictionary<string, List<string>> LiteralValuesByField { get; init; }
= new Dictionary<string, List<string>>();

public StylingConfidence Confidence { get; init; }

/// Which ready-made profile (UserExample/Extended/Literal) is closest to detected.
public string? ClosestKnownProfileName { get; init; }
}

public sealed record DetectedField
{
public required string FieldPath { get; init; } // "api.apim_name" or "api_operation.operation_id"
public int TemplatedOccurrences { get; init; }
public int LiteralOccurrences { get; init; }
public List<string> ObservedExpressions { get; init; } = []; // e.g. ["${apim_name}"]
public List<string> ObservedLiterals { get; init; } = []; // e.g. ["apim-company-dev"]
}

public enum StylingConfidence
{
/// >70% of fields in the file are interpolations.
HighlyTemplated,
/// 30-70% — mixed.
Mixed,
/// <30% — almost everything is literal.
MostlyLiteral ,
/// File is empty / no operations.
    Empty
}
```

### REV-1.3.2. Detection Algorithm

```
function Detect(document):
fields = {} #field_path -> DetectedField
allVars = set()

for group in document.ApiGroups:
for api in group.Apis:
for fieldName in ["apim_resource_group_name","apim_name","name",
"display_name","path","service_url","revision",
"product_id","subscription_required"]:
value = api.AstNode.Get(fieldName)
track(fields, "api."+fieldName, value, allVars)

for op in group.Operations:
for fieldName in ["operation_id","apim_resource_group_name","apim_name",
"api_name","display_name","method","url_template",
"status_code","description"]:
value = op.AstNode.Get(fieldName)
track(fields, "api_operation."+fieldName, value, allVars)

total_templated = sum(f.TemplatedOccurrences for f in fields.values())
total_literal = sum(f.LiteralOccurrences for f in fields.values())
total = total_templated + total_literal

if total == 0: confidence = Empty
elif total_templated/total > 0.7: confidence = HighlyTemplated
elif total_templated/total > 0.3: confidence = Mixed
else: confidence = MostlyLiteral

inferredProfile = buildProfileFromMostCommonExpressions(fields)
closestKnown = matchToKnownProfile(inferredProfile)

return DetectedProfile(inferredProfile, fields, allVars, ..., confidence, closestKnown)
```

`buildProfileFromMostCommonExpressions` — for each field, if `ObservedExpressions` contains an expression that occurs most often (>50% of non-empty expressions), it becomes the template for this field in the output profile. If a field in the file is always a literal, it is NOT included in ` ApiFieldTemplates` (i.e., new operations will also be literals).

### REV-1.3.3. Auto-grouping by `(apim_resource_group_name, api_name)`

Model:

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record ApimApiGroupKey
{
/// Structural representation - as it was in HCL (with ${...} or literal).
    public required string ApimResourceGroupNameRaw { get; init; }
public required string ApiNameRaw { get; init; }

    /// Resolved values (if variable was passed context ).
    public string? ApimResourceGroupNameResolved { get; init; }
public string? ApiNameResolved { get; init; }

    /// Use resolved values for equality if any, otherwise raw .
    public bool Equals(ApimApiGroupKey? other)
{
if (other is null) return false;
var thisRg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
var thisApi = ApiNameResolved ?? ApiNameRaw;
var otherRg = other.ApimResourceGroupNameResolved ?? other.ApimResourceGroupNameRaw;
var otherApi = other.ApiNameResolved ?? other.ApiNameRaw;
return string.Equals(thisRg, otherRg, StringComparison.OrdinalIgnoreCase)
&& string.Equals(thisApi, otherApi, StringComparison.OrdinalIgnoreCase);
}

public override int GetHashCode()
{
var rg = ApimResourceGroupNameResolved ?? ApimResourceGroupNameRaw;
var api = ApiNameResolved ?? ApiNameRaw;
return HashCode.Combine(rg.ToLowerInvariant(), api.ToLowerInvariant());
    }
}
```

### REV -1.3.4. Update ` ParsedApimDocument `

The following is added to the fields from §2.2:

```csharp
public sealed record ParsedApimDocument
{
// ... existing fields ...

/// Grouping api blocks and their operations by ( rg , name ).
/// KEY for sync : new operations from OpenAPI must go into the correct group.
    public IReadOnlyDictionary<ApimApiGroupKey, ApiGroupBundle> ApisByGroupKey { get; init; }
= new Dictionary<ApimApiGroupKey, ApiGroupBundle>();
}

public sealed record ApiGroupBundle
{
public required ApimApiGroupKey Key { get; init; }
public required ParsedApi Api { get; init; }
/// Operations where `apim_resource_group_name + api_name` matches Key.
public List<ParsedApiOperation> Operations { get; init; } = [];
}
```

**Construction algorithm** (in `ApimTerraformReaderService` after extraction):

```
for group in ApiGroups:
for op in group.Operations:
rg = op.AstNode.Get("apim_resource_group_name")?.StructuralText
apiName = op.AstNode.Get("api_name")?.StructuralText
key = ApimApiGroupKey(rg, apiName)
bundle = ApisByGroupKey.GetOrAdd(key)
bundle.Operations.Add(op)
    
for api in group.Apis:
rg = api.AstNode.Get("apim_resource_group_name")?.StructuralText
apiName = api.AstNode.Get("name")?.StructuralText
key = ApimApiGroupKey(rg, apiName)
bundle = ApisByGroupKey.GetOrAdd(key)
bundle . Api = api
```

### REV -1.3.5. Selecting a target group during sync

In `AppendOnlySynchronizer.Synchronize` now:

```
function Synchronize(parsed, newConfig, policy, strategy):
detector = ApimTemplateProfileDetector()
detected = detector.Detect(parsed)
    
    # Select a profile for new operations
    effectiveProfile = options.OverrideProfile ?? detected.InferredProfile
    
# Define the target group
targetKey = ApimApiGroupKey
{
ApimResourceGroupNameRaw = newConfig.Settings.StageGroupName,
ApiNameRaw = newConfig.Settings.ApiName
}
    
bundle = parsed.ApisByGroupKey.TryGetValue(targetKey, out var existing)
? existing
: createNewBundle(targetKey, ...)
    
# continue as before, but using effectiveProfile when constructing new HclObjects
```

This provides a UX invariant: the user does not specify "where to insert" - the system itself finds the correct `( rg , api _ name )` group based on the settings passed in the request.

---

## § REV -1.4. Updated ` OperationMatcher ` interaction with groups

When a file contains multiple API groups (e.g., ` bpc - api - dev` and ` bpc - internal - dev` ), operation matching must first filter only the desired group and then search for matches. Otherwise, the same ` GET / users` in different APIs will be matched as a "duplicate", even though they are different APIs .

Change in `OperationMatcher.Match`:

```csharp
MatchResult Match(
IReadOnlyList<OperationFingerprint> openApiFingerprints,
IReadOnlyList<OperationFingerprint> terraformFingerprints,
OperationMatchStrategy strategy,
ApimApiGroupKey? scopeKey = null); // NEW
```

If `scopeKey != null`, then before matching, from `terraformFingerprints` we leave only those whose `Fingerprint.ApiName` and `Fingerprint.ApiResourceGroup` (a new field in Fingerprint) match `scopeKey`.

Add to `OperationFingerprint`:
```csharp
public string? ApiResourceGroup { get; init; }
```

And in the matching keys:
```csharp
public enum OperationMatchKey
{
OperationId,
MethodAndUrl,
MethodAndUrlAndParams,
Tag,
ApiAndMethodAndUrl,
RgApiAndMethodAndUrl, // NEW — the strictest scope
Custom
}
```

---

## § REV -1.5. AST Comments and Operation Header Format

### REV -1.5.1. AST - nodes for comments

```csharp
namespace TerraformApi.Domain.Models.Hcl;

/// An element of the body of an object or array.
/// Can be either an assignment or a comment (which we want to keep).
public abstract record HclObjectItem : HclNode;

public sealed record HclAssignment : HclObjectItem
{
public required string Key { get; init; }
public required HclValue Value { get; init; }
public bool KeyIsQuoted { get; init; }
}

public sealed record HclComment : HclObjectItem
{
public required string Text { get; init; } // without prefix '#' / '//' / '/* */'
public required HclCommentKind Kind { get; init; }
    /// True if this is a block of comments (several in a row) - for grouping per entry.
    public bool IsLeading { get; init; }
}

public enum HclCommentKind { LineHash, LineSlash, Block }
```

**Changes in ` HclObject ` and ` HclArray `**:

```csharp
public sealed record HclObject : HclValue
{
// Previously: public List<HclAssignment> Assignments { get; init; }
// Became:
public List<HclObjectItem> Items { get; init; } = [];

    /// A convenient filter that returns only assignments .
    public IEnumerable<HclAssignment> Assignments => Items.OfType<HclAssignment>();

public HclValue? Get(string key) =>
Assignments.FirstOrDefault(a => a.Key == key)?.Value;
}

public sealed record HclArray : HclValue
{
// Array elements may be preceded by comments.
    public List<HclArrayItem> Items { get; init; } = [];
}

public sealed record HclArrayItem : HclNode
{
/// Comments immediately before this element.
    public List<HclComment> LeadingComments { get; init; } = [];
public required HclValue Value { get; init; }
}
```

### REV -1.5.2. Lexer and Parser - Comment Support

In §4.1 the lexer already skips comments. **Now we need to save them**:

- Lexer issues ` COMMENT ` tokens (previously skipped them).
- Parser when reading the body of ` HclObject ` or ` HclArray `:
- Accumulates "hanging" comments before the next assignment / item
- When an assignment is encountered , it wraps the accumulated comments in an HclArrayItem . LeadingComments or inserts them as individual HclComment s into an HclObject . Items immediately before the HclAssignment .

### REV -1.5.3. Writer - Comment Output

` HclWriterService ` on output:
- Each ` HclComment` in ` HclObject.Items` is output on a separate line with proper indentation.
- Each ` HclArrayItem . LeadingComments ` is displayed before the element itself, with the same indentation as the element.

`HclComment` output format:
```
LineHash: "#" + Text
LineSlash : "// " + Text
Block : "/* " + Text + " */"
```

If the source was `# foo `, the output will also be `# foo `. We don't change the style.

### REV -1.5.4. Pre-Operation Comment Format (User Request)

Each inserted operation receives a **block of multiple comments** as ` LeadingComments `:

```hcl
api_operations = [
# GET /users/{id} | op_id: getUserById
# display_name: "Get user by id" · source: OpenAPI · inserted: 2026-06-12 by sync
# placeholders to replace: ${stage_group_name}, ${apim_name}, ${api_name}, ${env}, ${operation_prefix}
{
operation_id = "${operation_prefix}-${env}"
apim_resource_group_name = "${stage_group_name}"
apim_name = "${apim_name}"
api_name = "${api_name}-${env}"
display_name = "Get user by id"
method = "GET"
url_template = "/users/{id}"
status_code = "200"
description = ""
},
]
```

**Strict**: The first line of the comment contains **only** `METHOD URL_TEMPLATE | op_id: <id>`. The rest of the details are on the second line. This is a user requirement.

If the operation has no placeholders ( LiteralProfile ) - the third line with ` placeholders to replace :` is skipped.

### REV-1.5.5. `IOperationCommentBuilder` and `OperationCommentSpec`

```csharp
namespace TerraformApi.Domain.Models.Sync;

public sealed record OperationCommentSpec
{
public required string Method { get; init; }
public required string UrlTemplate { get; init; }
public required string OperationId { get; init; }
public string? DisplayName { get; init; }
public required OperationCommentSource Source { get; init; }
public DateTime InsertedAt { get; init; } = DateTime.UtcNow;
public IReadOnlyList<string> PlaceholdersToReplace { get; init; } = [];
}

public enum OperationCommentSource
{
OpenApi,
Generated,
ManuallyAdded,
PreservedFromExisting
}

public interface IOperationCommentBuilder
{
/// Builds a list of comments according to the rules of § REV -1.5.4.
    List<HclComment> Build(OperationCommentSpec spec);

    /// Scans the HclObject (the newly inserted operation) and extracts all
/// unique ${ name } placeholders from its fields.
    IReadOnlyList<string> ExtractPlaceholders(HclObject operationNode);
}
```

Implementation of `OperationCommentBuilderService`:

```csharp
public List<HclComment> Build(OperationCommentSpec spec)
{
var comments = new List<HclComment>();

// Line 1: "GET /users/{id} | op_id: getUserById"
comments.Add(new HclComment
{
Kind = HclCommentKind.LineHash,
IsLeading = true,
Text = $" {spec.Method.ToUpperInvariant()} {spec.UrlTemplate} | op_id: {spec.OperationId}"
});

// Line 2: other information
var displayPart = string.IsNullOrEmpty(spec.DisplayName)
? ""
: $"display_name: \"{spec.DisplayName}\" · ";
var sourcePart = $"source: {spec.Source} · ";
var datePart = $"inserted: {spec.InsertedAt:yyyy-MM-dd}";
comments.Add(new HclComment
{
Kind = HclCommentKind.LineHash,
IsLeading = true,
Text = $"{displayPart}{sourcePart}{datePart}"
    });

// Line 3: only if there are placeholders
    if (spec.PlaceholdersToReplace.Count > 0)
{
comments.Add(new HclComment
{
Kind = HclCommentKind.LineHash,
IsLeading = true,
Text = $" placeholders to replace: {string.Join(", ", spec.PlaceholdersToReplace)}"
});
}

return comments;
}
```

### REV-1.5.6. Header block before the `api_operations` array

a block comment is added before the ` api_operations` array itself (or its new part when appending ) with a list of all unique placeholders in the file:

```hcl
# ============================================================================
# REPLACE BEFORE APPLY: define these variables in .tfvars or via -var:
# ${stage_group_name} ${apim_name} ${api_name} ${env} ${operation_prefix}
# ${frontend_host} ${company_domain} ${local_dev_host} ${local_dev_port}
# ============================================================================
api_operations = [​
...
]
```

If the file exists and the header already exists, we don't duplicate it. Detected by the REPLACE substring. BEFORE APPLY ` in the first comment before the array. If there is no header, but at least one new operation with placeholders is added, the header is added.

### REV -1.5.7. Extracting Placeholders

` ExtractPlaceholders ( HclObject )` recursively traverses all nested nodes and collects ` ReferencedExpressions ` from all ` HclInterpolation s`. Deduplicates. Sorts. Returns.

---

## § REV -1.6. Changes to ` IApimTerraformWriter ` and ` BuildFromConfiguration `

The signature of ` BuildFromConfiguration ` (§3.4) **changes**:

```csharp
public interface IApimTerraformWriter
{
string Write(ParsedApimDocument parsed, HclWriteOptions? options = null);

    /// Constructs a ParsedApimDocument using the template profile.
/// Each operation receives leading comments.
    ParsedApimDocument BuildFromConfiguration(
ApimConfiguration configuration,
BuildOptions options);
}

public sealed record BuildOptions
{
public ApimTemplateProfile Profile { get; init; } = ApimTemplateProfile.UserExampleProfile;
public IReadOnlyList<string>? ApiGroupParentPath { get; init; }
= new[] { "apis", "bpc_apis", "backend_apis" };
public bool AddOperationComments { get; init; } = true;
public bool AddReplaceBeforeApplyHeader { get; init; } = true;
public OperationCommentSource CommentSource { get; init; } = OperationCommentSource.OpenApi;
}
```

`BuildFromConfiguration` algorithm:

```
1. Create an empty HclDocument
2. Create a chain of HclObjects by ApiGroupParentPath
3. At the bottom, create HclAssignment with the ApiGroupName key (KeyIsQuoted if it contains ${...})
4. Inside add three assignments: product = [], api = [...], api_operations = [...]
5. For each api in configuration :
   a . Construct an HclObject using profile . ApiFieldTemplates
   b . Add to api []
6. For each operation :
   a. Generate operation_id via profile.OperationIdTemplate (with substitution)
b. Construct an HclObject using profile.OperationFieldTemplates
c. Generate LeadingComments via OperationCommentBuilder
d. Wrap in HclArrayItem { LeadingComments, Value }
e. Add to api_operations[]
7. If AddReplaceBeforeApplyHeader: collect all unique placeholders,
   Add a block comment before api_operations
8. Return ParsedApimDocument
```

---

## § REV -1.7. Changes in implementation phases (§6)

The existing phases remain, but are **supplemented**:

### Phase 1 (HCL Lexer/Parser/Writer) — EXPANDED

Additional subtasks:
- `HclComment.cs`, `HclObjectItem.cs`, `HclArrayItem.cs` (new AST types)
- Lexer issues COMMENT tokens (does not allow)
- Parser accumulates leading comments before each item
- Writer outputs comments with the correct indentation

Additional tests:
- C 1: Parsing `# foo \ n a = 1` → HclObject with two Items ( Comment , Assignment )
- C 2: Round - trip with comments (we write the same text)
- C 3: Comments before array elements
- C4: Multi-line block comment `/* ... */`

### Phase 2 - ADDED

New files:
- `ApimTemplateProfile.cs`
- `CorsTemplateVariables.cs`
- `DetectedProfile.cs`, `DetectedField.cs`, `StylingConfidence.cs`
- `ApimApiGroupKey.cs`, `ApiGroupBundle.cs`
- `OperationCommentSpec.cs`, `OperationCommentSource.cs`
- `BuildOptions.cs`, `ApplyProfileOptions.cs`

Tests: checking defaults of three ready-made profiles.

### Phase 3 ( Reader / Writer ) — EXTENDED

Additional subtasks:
- In ` ApimTerraformReaderService ` — construction of ` ApisByGroupKey `
- In ` ApimTerraformWriterService .BuildFromConfiguration` — use of ` BuildOptions`
- In `BuildFromConfiguration` — generation of leading comments via DI-injected `IOperationCommentBuilder`

Additional tests:
- T1: BuildFromConfiguration with UserExampleProfile → AST contains exactly the same placeholders as in Profile.ApiFieldTemplates
- T2: BuildFromConfiguration with LiteralProfile → AST without interpolations
- T 3: BuildFromConfiguration → each operation has 2 or 3 leading comments in the correct format
- T 4: Reader correctly constructs ApisByGroupKey for the user's working example (there is one group)
- T 5: Reader correctly builds ApisByGroupKey for a file with two different `( rg , api_name ) ` pairs

### Phase 4a (NEW) — `ApimTemplateProfileDetector`

File: `ApimTemplateProfileDetectorService.cs`.

Tests:
- DT1: Detect on empty document → Confidence=Empty
- DT2: Detect on user's working example → Confidence=HighlyTemplated, InferredProfile.ApiFieldTemplates contains the correct placeholders
- DT3: Detect on file with literals → Confidence=MostlyLiteral, InferredProfile.ApiFieldTemplates is empty
- DT4: Detect on mixed file → Confidence=Mixed, only fields with >50% templated occurrences are included in the InferredProfile
- DT5: AllReferencedVariables contains a unique set of all `${...}` from the file

### Phase 4b (NEW) - `OperationCommentBuilder`

Tests:
- CB 1: Build for OpenAPI operations without placeholders → 2 comments
- CB 2: Build with placeholders → 3 comments, the third one contains the correct list
- CB3: ExtractPlaceholders on HclObject with 5 different `${...}` → 5 unique names, sorted
- CB 4: The format of the first line is exactly ` METHOD URL | op _ id : ID ` (without extras)

### Phase 7 ( AppendOnlySynchronizer ) — EXTENDED

Additional subtasks:
- At the beginning of ` Synchronize` : call ` ApimTemplateProfileDetector` to detect the style
- If `SyncOptions.OverrideProfile == null` - use `detected.InferredProfile`
- Search for a target group using ` ApisByGroupKey` (not the first one that comes up)
- When inserting a new operation, generating leading comments via ` OperationCommentBuilder`
- When updating the ` api_operations` header (adding REPLACE BEFORE APPLY — detects an existing header

Additional tests:
- S 13: File exists with HighlyTemplated style + OpenAPI new operation → new operation in the same style
- S 14: File with MostlyLiteral + OpenAPI new operation → new operation also literal
- S 15: File with two api_groups , sync only to one → second group is byte-for-byte identical
- S 16: Each inserted operation has 2 or 3 leading - comments
- S 17: If there are placeholders, the REPLACE header has been added/updated BEFORE APPLY before array

### Phase 8 (Orchestrator) - EXPANDED

New methods:

```csharp
public sealed class ConversionOrchestratorService : IConversionOrchestrator
{
// ...
    
/// Analysis of an existing file without modifications.
    public AnalyzeResult Analyze(string existingTerraform);

/// Applying/removing a template profile.
public ApplyProfileResult ApplyProfile(
string existingTerraform,
ApimTemplateProfile profile,
ApplyProfileOptions options);

/// Sync with profile autodetect.
public SyncResult Sync(SyncRequest request);
}

public sealed record AnalyzeResult
{
public required bool Success { get; init; }
public DetectedProfile? DetectedProfile { get; init; }
public List<ApimApiGroupKey> ApiGroups { get; init; } = [];
public int TotalOperations { get; init; }
public List<DuplicateGroup> Duplicates { get; init; } = [];
public List<string> Errors { get; init; } = [];
}
```

---

## § REV -2. Changes in the MCP server

### REV -2.1. General Strategy

The existing six tools remain (with some clarifications), and three new ones are being added. All new tools use the same services from the Application layer—no logic should be present in the Mcp project (project rule).

### REV-2.2. New tools

#### A. `analyze_terraform_apim` — NEW

**Purpose**: The user has inserted a file and wants to understand what is in it.

**File**: `src/TerraformApi.Mcp/Tools/AnalyzeTool.cs`.

**Parameters**:
| Parameter | Type | Required | Description |
|---|---|---|---|
| ` existingTerraform ` | string | Yes | HCL file contents |

**Returns** (JSON):
```json
{
"success": true,
"apiGroups": [
{
"apimResourceGroupName": "${stage_group_name}",
"apiName": "${api_name}-${env}",
"operationCount": 12
}
],
"totalOperations": 12,
"detectedProfile": {
"confidence": "HighlyTemplated",
"closestKnownProfileName": "UserExampleProfile",
"fields": [
{ "fieldPath": "api.apim_name", "templatedOccurrences": 1, "literalOccurrences": 0, "observedExpressions": ["${apim_name}"] }
],
"allReferencedVariables": ["stage_group_name", "apim_name", "api_name", "env", "operation_prefix"]
},
"duplicates": [],
"warnings": []
}
```

**Implementation**: A thin wrapper around `ConversionOrchestrator.Analyze(existingTerraform)`.

#### B . ` sync _ openapi _ with _ terraform ` — NEW (main new command)

**Purpose**: main append - only sync . Replaces the old ` update_terraform_from_openapi` in the long term .

**File**: `src/TerraformApi.Mcp/Tools/SyncTool.cs`.

**Parameters**:
| Parameter | Type | Required | Description |
|---|---|---|---|
| ` openApiJson ` | string | None* | OpenAPI 3.x JSON (mutually exclusive with ` openApiUrl` ) |
| ` openApiUrl ` | string | No * | URL for fetch (already supported via IMPLEMENTATION_SUMMARY ) |
| ` existingTerraform ` | string | Yes | Contents of existing HCL |
| `environment` | string | Yes | `dev` / `staging` / `prod` (from ApimEnvironments) |
| `apiGroupName` | string | Yes | Which group to update (for disambiguation with multiple groups) |
| `templateProfileName` | string | No | `UserExampleProfile` / `ExtendedProfile` / `LiteralProfile` / `Auto` (default: `Auto` — detect from existing) |
| `mergePolicyJson` | string | No | JSON-serialized `MergePolicy` for fine-tuning |
| `matchStrategyJson` | string | None | JSON-serialized `OperationMatchStrategy` |
| `variableContextJson` | string | No | JSON `{"env":"dev","api_name":"bpc"}` for resolved-mode |
| `addOperationComments` | bool | No | Defaults to `true` |
| `addReplaceBeforeApplyHeader` | bool | No | Defaults to `true` |

`*` at least one of ` openApiJson ` / ` openApiUrl ` must be set.

**Returns** (JSON):
```json
{
"success": true,
"terraformConfig": "...full final HCL...",
"report": {
"operationsAdded": 2,
"operationsPreserved": 5,
"operationsEnriched": 1,
"operationsIdentical": 8,
"duplicates": [],
"warnings": [],
"diffs": [
{
"operationId": "${operation_prefix}-${env}",
"kind": "AddedFromOpenApi",
"fieldDiffs": []
}
]
},
"executionGraph": { ... },
"errors": []
}
```

#### C. `apply_template_profile` — NEW

**Purpose**: one-time conversion of "literals to templates" or vice versa.

**File**: `src/TerraformApi.Mcp/Tools/ApplyTemplateProfileTool.cs`.

**Parameters**:
| Parameter | Type | Required | Description |
|---|---|---|---|
| ` existingTerraform ` | string | Yes | Existing HCL |
| `direction` | string | Yes | `Templatize` (lit→tmpl) or `Resolve` (tmpl→lit) |
| `profileName` | string | Only with `Templatize` | Which profile to apply |
| `variableContextJson` | string | Only on `Resolve` | JSON of variable values |
| ` overwriteExisting ` | bool | No | Defaults to ` false ` (do not overwrite existing literals) |

**Returns**:
```json
{
"success": true,
"terraformConfig": "...",
"appliedChanges": [
"api.apim_name: \"apim-company-dev\" → ${apim_name}",
"api_operation[1].operation_id: \"list-users-dev\" → ${operation_prefix}-${env}"
  ],
" warnings ": []
}
```

### REV -2.3. Changes to existing tools

#### `convert_openapi_to_terraform` — UPDATE

Add parameters:
- `templateProfileName` (string, default `UserExampleProfile`) — which profile to use when generating
- `addOperationComments` (bool, default `true`)
- `addReplaceBeforeApplyHeader` (bool, default `true`)
- `apiGroupParentPathJson` (string, default `["apis","bpc_apis","backend_apis"]`) — wrapper structure

to the `[ Description ("...")]` attributes.

Implementation: internally call ` ConversionOrchestratorService.Convert` with ` BuildOptions` constructed from the new parameters. Old logic — backward - compat via defaults .

#### ` update _ terraform _ from _ openapi ` — UPDATE ( deprecated in the future)

Changes:
- Inside **delegate to ` SyncTool `** via ` ConversionOrchestratorService . Sync (...)` with default ` MergePolicy ` ( append - only ) and ` OperationMatchStrategy ` = `[ MethodAndUrl , OperationId ]`.
- ` templateProfileName = " Auto "` (detect from existing file).
- The old parameter signature is preserved ( backward - compat ); all 6 unit tests in ` UpdateToolTests ` remain green.
- Add a note to `[Description]`: `"Append-only merge — preserves all existing operations. For full control over policies, use sync_openapi_with_terraform."`.

#### ` transform _ environment ` — NO CHANGE

The logic remains the same. But internally, it should use the new IHclParser / IHclWriter approach instead of the current regex approach—this is a refactoring without changing external behavior. All 8 tests in TransformEnvironmentToolTests should remain green.

#### ` validate _ openapi _ for _ apim ` — NO CHANGES

#### `list_environment_presets` — NO CHANGE

#### `fetch_openapi_operations` — NO CHANGE

### REV -2.4. Summary table of MCP tools

| Tool | Status | Purpose |
|---|---|---|
| ` fetch _ openapi _ operations ` | unchanged | Fetching operations from OpenAPI URL |
| ` validate _ openapi _ for _ apim ` | unchanged | Validating the specification |
| ` list _ environment _ presets ` | unchanged | List of presets |
| `convert_openapi_to_terraform` | **updated** | + `templateProfileName`, + `addOperationComments` |
| `update_terraform_from_openapi` | **updated(delegate)** | Delegates in sync with append-only |
| ` transform _ environment ` | no changes from outside (refactoring inside) | Between environments |
| ` analyze _ terraform _ apim ` | **NEW** | Analyze an existing file |
| ` sync _ openapi _ with _ terraform ` | **NEW** | Master sync with full configuration |
| `apply_template_profile` | **NEW** | Templatize ↔ Resolve |

### REV -2.5. Registration in MCP server

File: `src/TerraformApi.Mcp/Program.cs`.

All classes marked with [ McpServerToolType ] will be included in the existing ` .WithToolsFromAssembly () ` call. No manual registration is required – just create ` AnalyzeTool` , ` SyncTool` , ` ApplyTemplateProfileTool` in ` src / TerraformApi.Mcp / Tools / ` with the correct attributes.

DI dependencies of new services (` IApimTemplateProfileDetector `, ` IApimTemplateProfileApplier `, ` IOperationCommentBuilder `, ` IAppendOnlySynchronizer `, etc.) are registered in ` Application . DependencyInjection ` and are automatically available to MCP tools.

### REV -2.6. Changes to `. vscode / mcp . json ` and ` claude _ desktop _ config . json`​

**Not required.** MCP client configs only specify server startup; the list of tools is discovered dynamically via the ` tools / list` JSON - RPC . After the client is restarted , new tools appear automatically.

### REV -2.7. MCP -layer tests

The following tests are added to the existing 67 in ` tests / TerraformApi.Mcp.Tests /`:

| File | Number of tests | What it covers |
|---|---|---|
| ` AnalyzeToolTests . cs ` | 8 | Empty file, user's working example, file with duplicates, file with two groups, MostlyLiteral , HighlyTemplated , valid ` apiGroups `, error for invalid HCL |
| ` SyncToolTests . cs ` | 12 | Append - only basic, adding 1 new, OpenAPI empty, both empty, ambiguous match , filling description via EnrichIfMissing , appending to request . header collections , checking comments in output, checking for REPLACE BEFORE APPLY header 'a, checking roundtrip other groups, using ` templateProfileName = Auto `, using overridden profile |
| ` ApplyTemplateProfileToolTests . cs ` | 6 | Templatize direction, Resolve direction, OverwriteExisting = true , OverwriteExisting = false , unknown profile → error, missing variable → warning |
| ` ConvertToolTests . cs ` (extend) | +5 | Convert with UserExampleProfile , with LiteralProfile , with ExtendedProfile , with ` addOperationComments = false `, with custom ` apiGroupParentPath ` |
| ` UpdateToolTests . cs ` (extend) | +3 | Update delegates to sync with correct defaults, existing 6 tests remain green, new operations get comments |

**Total**: 67 → **101 tests** in the MCP layer.

---

## §REV-3. Updated Definition of Done

The following is added to points §8:

- [ ] New AST types (HclComment, HclObjectItem, HclArrayItem) - round-trip with comments on `example-existing.tf`
- [ ] The parser saves comments, the writer outputs them back with the correct indentation
- [ ] ` ApimTemplateProfile . UserExampleProfile ` exists and is being tested
- [ ] ` ApimTemplateProfile . ExtendedProfile ` exists with additional placeholders from § REV -1.2.2
- [ ] ` ApimTemplateProfile . LiteralProfile ` exists
- [ ] ` IApimTemplateProfileDetector ` correctly detects the style on the working example ( Confidence = HighlyTemplated , ClosestKnownProfileName =" UserExampleProfile ")
[ ] ` ApisByGroupKey ` in ` ParsedApimDocument ` correctly groups API operations by `( rg , api_name ) `
- [ ] Each inserted operation has 2-3 leading comments in the format § REV -1.5.4
- [ ] Hat ` REPLACE BEFORE APPLY ` is added automatically if placeholders appear in the file
- [ ] When synchronizing a file without redefining a profile, new operations automatically receive the style of existing ones
- [ ] 3 new MCP tools (`analyze_terraform_apim`, `sync_openapi_with_terraform`, `apply_template_profile`) are registered and working
- [ ] Existing MCP tools continue to work (all 67 current tests are green)
- [ ] Added 34 new MCP tests (see § REV -2.7)
- [ ] The README section about the MCP server has been updated with a description of new tools and their parameters

---

## § REV -4. Final Acceptance Scenario - Updated Version

**Scenario A : Sync without specifying a profile ( auto - detect )**

Given:
- ` existingTerraform ` = user's working example (as is)
- ` openApiJson ` = OpenAPI with operations: GET / health (new), GET / users (new), GET / users /{ id } (already in TF )
- ` apiGroupName ` = `${ api _ group _ name }` (as in file)
- `templateProfileName` = not set (default `Auto`)

Expected:
1. The output HCL is parsable and valid.
The ` api_operations` array now has **2** new entries (for `/ health` and `/ users` ), each with its own leading comment block.
3. The first line of the comment of the first new operation = exactly: `# GET / health | op _ id : < generated _ id >`.
4. The existing `/ users /{ id }` operation is unchanged.
5. Profile used = ` UserExampleProfile ` ( auto - detected ), new operations contain `${ operation _ prefix }-${ env }`, `${ stage _ group _ name }`, etc.
The ` api_operations` array has a header comment ` REPLACE` or has been added before it . BEFORE APPLY :` with all unique placeholders.
7. `SyncReport.OperationsAdded = 2`, `OperationsIdentical = 1`.

**Scenario B: Conversion from scratch**

Given:
- ` openApiJson ` = OpenAPI with 3 operations
- `existingTerraform` = empty
- `templateProfileName` = `UserExampleProfile`
- `addOperationComments` = `true`

Expected:
1. The output is valid HCL with the structure `apis.bpc_apis.backend_apis."${api_group_name}" = {...}`.
2. It has exactly 3 operations in ` api_operations` .
3. Each operation has 3 leading comments (METHOD URL | op_id, source, placeholders).
4. Before the array there is a header `REPLACE BEFORE APPLY:`.
5. All required fields (`apim_resource_group_name`, `apim_name`, `name`, `display_name`, `path`, `service_url`, `revision`) are interpolations from the profile.

**Scenario C : Analyze **

Given:
- ` existingTerraform ` = user's working example

Expected:
1. Response.success = true
2. Response.apiGroups = 1 element
3. Response.totalOperations = 1 (as in the example)
4. Response.detectedProfile.confidence = `HighlyTemplated`
5. Response.detectedProfile.closestKnownProfileName = `UserExampleProfile`
6. Response.detectedProfile.allReferencedVariables contains at least: `stage_group_name`, `apim_name`, `api_name`, `env`, `operation_prefix`, `operation_path`, `frontend_host`, `company_domain`, `local_dev_host`, `local_dev_port`, `api_path_prefix`, `api_path_suffix`, `api_gateway_host`, `api_version`, `backend_service_path`, `api_revision`, `product_id`, `api_display_name`, `operation_display_name`.

---

**End of REVISION 1.**

Total additional work for REVISION 1: 4–6 hours beyond the basic plan. Total: **14–24 hours** for full implementation of the Convert + Sync + Analyze + Profile + MCP tools.

