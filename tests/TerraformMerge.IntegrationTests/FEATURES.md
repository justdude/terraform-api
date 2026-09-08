# Terraform Merge — Feature Test Plan

Every feature of the **Terraform Merge** desktop application, the integration
test that exercises it, and its status. Statuses are filled from an actual run of
`dotnet test tests/TerraformMerge.IntegrationTests`.

Legend: ✅ pass · ❌ fail (see Bugs) · ⬜ not yet run

## How this maps

- **CLI** features run the real `TerraformMerge.exe` as a process (`Cli/CliProcessTests.cs`).
- **Workflow** features drive the engine through real file I/O exactly as the
  window's buttons do (`Workflows/MergeWorkflowTests.cs`,
  `Workflows/EnvironmentWorkflowTests.cs`).
- **UI** features drive a real `MainForm` on an STA thread through its own handlers
  (`Ui/MainFormDriverTests.cs`).

## Features

| ID | Feature | Test | Status |
|----|---------|------|--------|
| F1  | CLI `convert`: OpenAPI file → APIM Terraform, exit 0 | `Convert_OpenApiFile_WritesTerraform_Exit0` | ✅ |
| F2  | CLI `convert`: omitted settings become `{placeholder}` tags | `Convert_OmittedSettings_EmitPlaceholderTags` | ✅ |
| F3  | CLI `convert`: invalid OpenAPI → exit 1, no output | `Convert_InvalidOpenApi_Exit1` | ✅ |
| F4  | CLI `merge`: TF target, append only missing ops | `Merge_TerraformTarget_AppendsOnlyMissing_Exit0` | ✅ |
| F5  | CLI `merge`: OpenAPI target auto-detected | `Merge_OpenApiTarget_AutoDetected_AppendsNewOperation` | ✅ |
| F6  | CLI `merge`: `--out` different path leaves original untouched | `Merge_OutDifferentPath_LeavesOriginalUntouched` | ✅ |
| F7  | CLI `merge`: no `--out` rewrites original in place | `Merge_NoOutArg_RewritesOriginalInPlace` | ✅ |
| F8  | CLI `merge`: `--threshold` accepted | `Merge_ThresholdOption_Accepted` | ✅ |
| F9  | CLI `help` / `--help` / `-h` → exit 0 | `Help_Exit0` | ✅ |
| F10 | CLI unknown verb → exit 1 | `UnknownVerb_Exit1` | ✅ |
| F11 | CLI missing required arg → exit 1 | `Convert_MissingRequiredArg_Exit1` | ✅ |
| F12 | CLI `merge`: CRLF original stays CRLF | `Merge_CrlfOriginal_OutputStaysCrlf` | ✅ |
| F13 | Load Original (Terraform) lists operations | `LoadOriginal_ListsOperations` | ✅ |
| F14 | Load Target (Terraform and OpenAPI) | `LoadTarget_TerraformAndOpenApi` | ✅ |
| F15 | Compute Diff classifies Added, omits equivalent | `ComputeDiff_ClassifiesAddedAndOmitsEquivalent` | ✅ |
| F15b| Compute Diff (API mode): new method on same route added | `ComputeDiff_ApiMode_NewMethodOnSameRouteIsAdded` | ✅ |
| F16 | Add to Original skips equivalents already present | `AddToOriginal_SkipsEquivalentsAlreadyPresent` | ✅ |
| F17 | Remove from Original drops the operation | `RemoveFromOriginal_DropsSelectedOperation` | ✅ |
| F18 | Save Original to disk; originals byte-for-byte | `SaveOriginal_ToDisk_AppendsAndPreservesOriginalsVerbatim` | ✅ |
| F19 | Edit: accept value from either side; one-line diff; id fixed | `EditOperation_AcceptValueFromOtherSide_ChangesExactlyOneLine` | ✅ |
| F20 | Edit: quote + interpolation + backslash → valid HCL | `EditOperation_QuoteAndInterpolation_ProducesParseableHcl` | ✅ |
| F22 | Multi-group: unchanged byte-for-byte; edit isolated to its group | `MultiGroup_RewriteUnchanged_IsByteForByte_AndEditIsolated` | ✅ |
| F23 | Merge field catalog unions both sides' values | `MergeFieldCatalog_UnionsBothSides` | ✅ |
| F24 | Convert via facade produces config | `Convert_Facade_ProducesConfigFromOpenApi` | ✅ |
| F25 | MainForm builds its control tree | `MainForm_BuildsControlTree_WithoutError` | ✅ |
| F26 | Operation editor dialog builds | `OperationEditorDialog_BuildsWithoutError` | ✅ |
| F27 | Mode toggle updates Target pane title | `ModeToggle_UpdatesTargetPaneTitle` | ✅ |
| F28 | Load → Compute Diff → Save through the form handlers | `LoadComputeDiffSave_ThroughFormHandlers` | ✅ |
| F29 | Loading a pane preselects the environment its document is in | `LoadingAPane_PreselectsTheEnvironmentTheDocumentIsIn` | ✅ |
| F30 | Add to Original re-stamps the copy for the Original's environment | `AddToOriginal_ReStampsTheCopyForTheOriginalsEnvironment` | ✅ |
| F31 | Set selected moves the selected rows to another environment | `SetSelected_MovesTheSelectedOriginalRowsToAnotherEnvironment` | ✅ |
| F32 | Row menu rebuilds its environment list every time it opens | `RowMenu_RebuildsItsEnvironmentListEveryTimeItOpens` | ✅ (found BUG-3) |
| F33 | Loaded documents report their environments and profiles | `LoadedDocumentsReportTheirEnvironments` | ✅ |
| F34 | dev → qa: qa gains the operations it was missing, as qa operations | `MissingDevOperationsAreAddedToQaAsQaOperations` | ✅ |
| F35 | Environment set in place rewrites only that operation's lines | `SettingAnOperationsEnvironmentInPlaceRewritesOnlyThatOperation` | ✅ |
| F36 | CLI `merge --env <e>`: every appended operation is re-stamped for `<e>` | `Merge_EnvFlag_AppendsTheDevOperationsAsQa` | ✅ |
| F37 | CLI `merge --env` with no value → exit 1, nothing written | `Merge_EnvFlag_WithoutAValue_Exit1_AndWritesNothing` | ✅ |

## Edge cases / robustness (`Cli/CliEdgeCaseTests.cs`)

| ID | Scenario | Test | Status |
|----|----------|------|--------|
| E1 | Malformed original → exit 1, not a crash | `Merge_MalformedOriginal_Exit1_NotCrash` | ✅ |
| E2 | Self-merge (target ⊆ original) → byte-for-byte identical | `Merge_TargetSubsetOfOriginal_ByteForByteIdentical` | ✅ |
| E3 | Append preserves the api-block policy heredoc | `Merge_PreservesPolicyHeredoc` | ✅ |
| E4 | `convert` output is itself reloadable HCL | `Convert_Output_IsReparseableTerraform` | ✅ (found BUG-1) |
| E5 | Merge is idempotent (second pass adds nothing) | `Merge_Idempotent_SecondPassAddsNothing` | ✅ |
| E6 | OpenAPI 3.1 is downgraded and converts | `Convert_OpenApi31_Succeeds` | ✅ |

## Not covered here (and why)

- **Browse… file dialogs, Convert OpenAPI… dialog, the Edit dialog opened from
  the window, and the Save path-mismatch warning** open a modal dialog / MessageBox
  that blocks a headless STA thread. The underlying logic is covered: the editor
  by F19/F20 and the unit suite; conversion by F24; the Save guard by unit review.
- **`convert --openapi <url>` / `merge --target <url>`** would require live network.

## Visual test (screenshots)

`Screenshots/capture-app.ps1` launches the real `TerraformMerge.exe`, drives it
with UI Automation, and captures the actual on-screen window:

| Shot | State | Verified |
|------|-------|----------|
| `01-empty-window.png` | window at startup | ✅ renders |
| `02-after-compute-diff.png` | Original/Diff/Target populated, `Diff: 1 to add, 0 changed` | ✅ correct |
| `03-operation-editor.png` | per-field editor (operation_id fixed, every field an editable combo) | ✅ correct |

Run: `powershell -File tests/TerraformMerge.IntegrationTests/Screenshots/capture-app.ps1`
(images default to `%TEMP%\tfmerge-shots`). It found BUG-2 below.

## Bugs found

### BUG-1 — `convert` with an omitted `--api-group` emits unparseable HCL (FIXED)

- **Surfaced by:** `Cli/CliEdgeCaseTests.Convert_Output_IsReparseableTerraform` (E4)
  — feeding a placeholder `convert` output back through `merge` failed to load.
- **Symptom:** with `--api-group` omitted, the generated config's root was
  `{api-group} = {` — a bare object key beginning with `{`. That is invalid HCL:
  the tool's own parser (and `terraform`) reject it with
  `Expected assignment or comment, found '{'`. Every other placeholder sits inside
  a quoted string, so only the api-group placeholder — used as a bare *key* —
  corrupted the whole document. The generated "template" could not be opened,
  `fmt`'d, or reloaded until the user manually replaced the tag.
- **Root cause:** `TerraformGeneratorService.Generate` wrote the group key
  unquoted (`{ApiGroupName} = {`).
- **Fix:** quote the group key when it is not a legal bare HCL identifier
  (`FormatGroupKey`), so `{api-group}` becomes `"{api-group}"`. Valid names
  (`orders-api-group`) stay bare, unchanged. Regression-locked by E4 (the output
  now round-trips through `merge`).
- **Status:** fixed; 2 pre-existing assertions that expected the invalid bare
  placeholder key were corrected to the quoted form.

### BUG-2 — button captions clipped ("Load" → "Loa") (FIXED)

- **Surfaced by:** the screenshot pass (`02-after-compute-diff.png`).
- **Symptom:** each pane's **Load** button rendered as "Loa" and the two
  **◄ Add to Original** buttons were clipped to "…Origina" — the fixed 60px Load
  column and the AutoSize button's measured width were a few pixels short of the
  caption.
- **Fix:** the browse/Load columns are now `AutoSize`, and the Load / browse /
  Add buttons `AutoSize` with a little right padding, so captions always fit
  (confirmed by re-capturing `02`).
- **Status:** fixed; cosmetic (low severity), no data impact.

### BUG-3 — the environment bar's controls were clipped, and its menu threw on reopen (FIXED)

- **Surfaced by:** the screenshot pass again (`02-after-compute-diff.png`), and
  by `RowMenu_RebuildsItsEnvironmentListEveryTimeItOpens` (F32).
- **Symptom:** the pane's new **Env:** row was given a fixed 32px table row (and
  the move bar 34px), which is less than an AutoSize button plus the panel's and
  the button's own margins — the combo and **Set selected** were sliced off at
  about 60% height, and the **◄ Add to Original** caption lost its bottom pixels.
  Separately, reopening a row's context menu threw: the rebuild disposed the
  previous round's environment items while enumerating them, and disposing a
  `ToolStripItem` removes it from the collection being enumerated.
- **Fix:** both bars' rows are `AutoSize` and both panels `AutoSize` with
  `GrowAndShrink`, so a row is exactly as tall as its controls need; the menu
  rebuild copies the items out (`.ToArray()`) before disposing them. The editor
  dialog also grew a row taller than its header allowed — its header row and
  label column were widened to fit "Resource group" and the two-line hint.
- **Status:** fixed; confirmed by re-capturing `02`/`03` and by F32, which fails
  against the pre-fix menu code.

_No other feature failed: F1–F37 and E1–E6 pass; the three screenshots render
correctly._
