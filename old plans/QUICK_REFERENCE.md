# Operation Tracking System - Quick Reference Card

## One-Page Overview

### What Problem Are We Solving?

```
❓ When I generate/update Terraform APIM config from OpenAPI...
   - Which methods actually got processed?
   - Why was operation X included but Y excluded?
   - Did my custom operations get preserved?
   - What changed compared to the previous config?
   - Is there a dependency between operations A and B?

✅ WITH TRACKING SYSTEM:
   - See every operation, its status, and WHY
   - Get visualizations showing the complete picture
   - Audit trail of all changes
   - Export reports for team review/approval
```

---

## Models to Create

### 3 Core Models

```csharp
1️⃣  OperationExecutionNode
	├─ OperationId: string
	├─ Method: string (GET, POST, etc.)
	├─ UrlTemplate: string
	├─ Status: OperationStatus (Included|Modified|Excluded|Blocked...)
	├─ Source: OperationSource (OpenApi|ExistingConfig|Custom|Generated)
	├─ DependsOnOperationIds: List<string>
	├─ ReferencedBy: List<string>
	└─ Reason: string

2️⃣  OperationExecutionGraph
	├─ ApiGroupName: string
	├─ Nodes: Dictionary<string, OperationExecutionNode>
	├─ Issues: List<ExecutionGraphIssue>
	└─ Statistics: ExecutionGraphStatistics

3️⃣  OperationTrackingReport
	├─ TotalOperationsTracked: int
	├─ OperationsByStatus: Dictionary<Status, List<string>>
	├─ OperationsBySource: Dictionary<Source, List<string>>
	├─ Deltas: List<OperationDelta>  // What changed
	└─ Dependencies: List<OperationDependency>
```

---

## Enums to Define

```csharp
OperationStatus
├─ Pending
├─ Included ✓
├─ Modified ⚠️
├─ Excluded ❌
├─ Blocked 🚫
├─ Skipped ⏭️
└─ Deprecated ⚡

OperationSource
├─ OpenApi (new spec)
├─ ExistingConfig (old terraform)
├─ Custom (manually added)
└─ Generated (auto-generated)

OperationChangeType
├─ Added
├─ Removed
├─ MethodChanged (GET→POST)
├─ UrlTemplateChanged (/user→/users)
├─ ParametersChanged
└─ Multiple

VisualizationFormat
├─ Json
├─ Mermaid (diagrams)
├─ PlantUML
├─ Graphviz
└─ Csv
```

---

## Data Flow Diagram

```
INPUT
─────

OpenAPI JSON ─┐
			  │
			  ├─→ IOperationExecutionGraphBuilder
			  │   ├─ BuildFromConfiguration()
			  │   ├─ BuildMergedGraph()
			  │   ├─ AnalyzeDependencies()
			  │   ├─ ValidateExecutionSequence()
			  │   ├─ ExportToVisualization()
			  │   └─ GenerateTrackingReport()
			  │
Existing TF ──┤
			  │
			  └─→ OperationExecutionGraph
				  ├─ Nodes[]
				  ├─ Issues[]
				  └─ Statistics{}

OUTPUT
──────

Enhanced ConversionResult
├─ TerraformConfig: string (same as before)
├─ Configuration: ApimConfiguration (same as before)
├─ ExecutionGraph: OperationExecutionGraph ◄─ NEW
├─ TrackingReport: OperationTrackingReport ◄─ NEW
├─ Warnings: string[]
└─ Errors: string[]
```

---

## Implementation Checklist

### Phase 1: Create Models (1-2 hours)

- [ ] Create `OperationExecutionGraph.cs`
  - [ ] OperationExecutionNode
  - [ ] OperationStatus (enum)
  - [ ] OperationSource (enum)
  - [ ] ExecutionGraphIssue
  - [ ] IssueLevel (enum)
  - [ ] ExecutionGraphStatistics

- [ ] Create `OperationTrackingReport.cs`
  - [ ] OperationTrackingReport
  - [ ] OperationDelta
  - [ ] OperationChangeType (enum)
  - [ ] OperationDependency
  - [ ] DependencyType (enum)

- [ ] Update `ConversionResult.cs`
  - Add: `OperationExecutionGraph? ExecutionGraph { get; init; }`
  - Add: `OperationTrackingReport? TrackingReport { get; init; }`

### Phase 2: Interface & Service (2-3 hours)

- [ ] Create `IOperationExecutionGraphBuilder.cs` interface
  - [ ] BuildFromConfiguration()
  - [ ] BuildMergedGraph()
  - [ ] AnalyzeDependencies()
  - [ ] ValidateExecutionSequence()
  - [ ] ExportToVisualization()
  - [ ] GenerateTrackingReport()
  - [ ] VisualizationFormat enum

- [ ] Create `OperationExecutionGraphBuilderService.cs`
  - [ ] Implement BuildFromConfiguration()
  - [ ] Implement BuildMergedGraph()
  - [ ] Implement AnalyzeDependencies()
  - [ ] Implement ValidateExecutionSequence()
  - [ ] Implement ExportToJson()
  - [ ] Implement ExportToMermaid()
  - [ ] Implement ExportToCsv()
  - [ ] Helper: ExtractOperationsFromTerraform()
  - [ ] Helper: UpdateStatistics()
  - [ ] Helper: GroupOperationsByStatus()
  - [ ] Helper: GroupOperationsBySource()

### Phase 3: Integration (1 hour)

- [ ] Update `DependencyInjection.cs`
  - Add: `services.AddSingleton<IOperationExecutionGraphBuilder, OperationExecutionGraphBuilderService>();`

- [ ] Update `ConversionOrchestratorService.cs`
  - Add field: `private readonly IOperationExecutionGraphBuilder _graphBuilder;`
  - Add to constructor parameter
  - In `Convert()` method:
	- Add: `var graph = _graphBuilder.BuildFromConfiguration(configuration);`
	- Add: `_graphBuilder.AnalyzeDependencies(graph);`
	- Add: `var trackingReport = _graphBuilder.GenerateTrackingReport(graph);`
	- Set: `result.ExecutionGraph = graph;`
	- Set: `result.TrackingReport = trackingReport;`

### Phase 4: Testing (1-2 hours)

- [ ] Build solution
- [ ] Write unit test for BuildFromConfiguration()
- [ ] Write unit test for BuildMergedGraph()
- [ ] Write integration test
- [ ] Test visualization exports

---

## Usage Examples

### Example 1: Basic Tracking
```csharp
var result = orchestrator.Convert(openApiJson, settings);
var stats = result.ExecutionGraph?.Statistics;

Console.WriteLine($"Total: {stats?.TotalOperations}");
Console.WriteLine($"Included: {stats?.IncludedOperations}");
Console.WriteLine($"Modified: {stats?.ModifiedOperations}");
Console.WriteLine($"Excluded: {stats?.ExcludedOperations}");
```

### Example 2: List Operations by Status
```csharp
foreach (var (status, opIds) in result.TrackingReport?.OperationsByStatus ?? new())
{
	Console.WriteLine($"\n{status}:");
	foreach (var opId in opIds)
	{
		var node = result.ExecutionGraph.Nodes[opId];
		Console.WriteLine($"  {node.Method} {node.UrlTemplate}");
		Console.WriteLine($"    Reason: {node.Reason}");
	}
}
```

### Example 3: Export Diagram
```csharp
var mermaidDiagram = graphBuilder.ExportToVisualization(
	result.ExecutionGraph,
	VisualizationFormat.Mermaid);

Console.WriteLine(mermaidDiagram);
// Output:
// ```mermaid
// graph TD
//     GET_users["✓ GET /users"]
//     POST_users["✓ POST /users"]
// ```
```

### Example 4: Check Changes
```csharp
foreach (var delta in result.TrackingReport?.Deltas ?? new())
{
	Console.WriteLine($"{delta.OperationId}: {delta.ChangeType}");
	Console.WriteLine($"  From: {delta.FromValue}");
	Console.WriteLine($"  To: {delta.ToValue}");
}
```

### Example 5: Detect Issues
```csharp
var issues = result.ExecutionGraph?.Issues ?? new();
foreach (var issue in issues.Where(i => i.Level == IssueLevel.Error))
{
	Console.WriteLine($"❌ {issue.Message}");
	Console.WriteLine($"   Suggestion: {issue.Recommendation}");
}
```

---

## Key Decisions Made

| What | Decision | Why |
|------|----------|-----|
| **Model Approach** | Separate Node + Graph | Allows independent operation tracking |
| **Status Enum** | 7 statuses | Covers all scenarios: new, changed, preserved, blocked |
| **Source Tracking** | Enum (4 options) | Answers "where did this operation come from?" |
| **Change Detection** | Delta pattern | Easy to audit and export |
| **Dependencies** | Optional list | Future-proof for complex analysis |
| **Export Formats** | Multiple (JSON, Mermaid, CSV) | Flexible consumption (APIs, docs, sheets) |
| **Statistics** | Auto-calculated | Always accurate, no manual sync |
| **Non-invasive** | Additive to ConversionResult | Zero breaking changes |

---

## Testing Scenarios

```
Test Case 1: Fresh OpenAPI (No existing config)
├─ Input: 5 operations in OpenAPI, no existing terraform
├─ Expected:
│   ├─ All 5 operations: Status = Included, Source = OpenApi
│   ├─ Total stats: Included=5, Excluded=0, Modified=0
│   └─ No Deltas
└─ Verify: ✓

Test Case 2: Merge with preservation
├─ Input: 5 new operations + 3 existing + 2 custom (not in OpenAPI)
├─ Expected:
│   ├─ 5 new: Status = Included, Source = OpenApi
│   ├─ 3 existing: Status = Modified or Included, Source = ExistingConfig
│   ├─ 2 custom: Status = Excluded, Source = ExistingConfig
│   └─ Stats: Included=8, Modified=3, Excluded=2
└─ Verify: ✓ (custom preserved)

Test Case 3: Operation deleted
├─ Input: Old had DELETE /users, new doesn't
├─ Expected:
│   ├─ DELETE: Status = Excluded
│   └─ Delta: OperationChangeType = Removed
└─ Verify: ✓

Test Case 4: URL changed
├─ Input: Old: PUT /user/{id}, New: PUT /users/{id}
├─ Expected:
│   ├─ Status = Modified
│   ├─ Delta.ChangeType = UrlTemplateChanged
│   ├─ Delta.FromValue = "/user/{id}"
│   └─ Delta.ToValue = "/users/{id}"
└─ Verify: ✓

Test Case 5: Visualization export
├─ Input: 3 operations
├─ Expected:
│   ├─ Mermaid: Valid diagram syntax
│   ├─ CSV: Valid CSV format
│   └─ JSON: Valid JSON structure
└─ Verify: ✓
```

---

## Performance Considerations

| Operation | Estimated Time |
|-----------|-----------------|
| Build graph (50 ops) | ~5 ms |
| Analyze dependencies | ~2-5 ms |
| Validate sequence | ~1-2 ms |
| Export to JSON (50 ops) | ~3-5 ms |
| Export to Mermaid (50 ops) | ~5-10 ms |
| Export to CSV (50 ops) | ~3-5 ms |
| **Total for typical API** | **~20-40 ms** |

Negligible impact on conversion time (typically 500-1000 ms)

---

## Common Issues & Solutions

| Issue | Solution |
|-------|----------|
| Graph shows all Excluded | Check: Are you comparing with existing terraform? |
| Statistics don't match | Ensure ExtractOperationsFromTerraform() regex works |
| Mermaid syntax invalid | Test diagram on https://mermaid.live |
| Circular dependency detected | That's expected - graph handles it |
| Missing operations in report | Verify all operations added to Nodes dict |

---

## File Creation Order

```
1. Create Models (independent)
   └─ OperationExecutionGraph.cs
   └─ OperationTrackingReport.cs

2. Update Result Model
   └─ ConversionResult.cs

3. Create Interface
   └─ IOperationExecutionGraphBuilder.cs

4. Create Service
   └─ OperationExecutionGraphBuilderService.cs

5. Update DI
   └─ DependencyInjection.cs

6. Integrate
   └─ ConversionOrchestratorService.cs

7. Test
   └─ Create unit tests
```

---

## Success Indicators

✅ **You'll know it's working when:**
- New models compile without errors
- Graph builder service is registered and injectable
- ConversionResult includes ExecutionGraph and TrackingReport
- Statistics calculate correctly
- Visualizations generate valid output
- Tracking report shows proper deltas
- Merge scenarios preserve custom operations

✅ **You'll know it's useful when:**
- You can export a diagram showing operation flow
- Team can approve changes before applying
- Audit trail answers "what changed and why"
- No operations are silently lost
- Change detection works accurately

---

## Next Steps

1. ✅ Read this document (you're here!)
2. 📖 Read `OPERATION_TRACKING_IMPLEMENTATION.md`
3. 💻 Copy-paste code into your project
4. 🔨 Implement the 4 files + 3 modifications
5. ✔️ Build and test
6. 🚀 Deploy and get insights!

**Estimated total time: 2-3 hours**

---

*For detailed implementation instructions, see OPERATION_TRACKING_IMPLEMENTATION.md*
