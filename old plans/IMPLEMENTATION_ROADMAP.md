# Operation Tracking System - Implementation Roadmap

## Decision Point

**Before you start: Decide your scope**

- [ ] **Option A - Full Implementation** (2-3 hours)
  - Models + Interface + Service + Integration + Basic Visualizations
  - Complete tracking and reporting
  - All export formats

- [ ] **Option B - Core Only** (1-2 hours)
  - Models + Interface + Service + Integration (NO visualizations)
  - Basic graph building and statistics
  - JSON export only

- [ ] **Option C - Minimal** (0.5-1 hour)
  - Models + ConversionResult update
  - Graph building in orchestrator (inline, no separate service)
  - Statistics only, no exports
  - Add service later

**Recommended for first version: Option B (Core Only)**
- Gets you 80% of value with 50% effort
- Easier to test and debug
- Can add visualizations later as enhancement

---

## Phase 1: Create Model Files

### Task 1.1: Create OperationExecutionGraph.cs

**File**: `src/TerraformApi.Domain/Models/OperationExecutionGraph.cs`

```
✅ Copy full content from OPERATION_TRACKING_IMPLEMENTATION.md
✅ Verify all enums are included
✅ Check no typos or missing properties
✅ Build solution - should compile
```

**Checklist**:
- [ ] File created in correct folder
- [ ] Namespace: `TerraformApi.Domain.Models`
- [ ] 6 classes/records: OperationExecutionNode, OperationStatus, OperationSource, OperationExecutionGraph, ExecutionGraphIssue, IssueLevel
- [ ] Compiles without errors

### Task 1.2: Create OperationTrackingReport.cs

**File**: `src/TerraformApi.Domain/Models/OperationTrackingReport.cs`

```
✅ Copy full content from OPERATION_TRACKING_IMPLEMENTATION.md
✅ Verify all enums included
✅ Build solution - should compile
```

**Checklist**:
- [ ] File created in correct folder
- [ ] Namespace: `TerraformApi.Domain.Models`
- [ ] 5 classes/records: OperationTrackingReport, OperationDelta, OperationChangeType, OperationDependency, DependencyType
- [ ] Compiles without errors

### Task 1.3: Update ConversionResult.cs

**File**: `src/TerraformApi.Domain/Models/ConversionResult.cs`

```
BEFORE:
-------
public sealed class ConversionResult
{
	public bool Success { get; init; }
	public string TerraformConfig { get; init; } = "";
	public ApimConfiguration? Configuration { get; init; }
	public List<string> Warnings { get; init; } = [];
	public List<string> Errors { get; init; } = [];
}

AFTER:
------
public sealed class ConversionResult
{
	public bool Success { get; init; }
	public string TerraformConfig { get; init; } = "";
	public ApimConfiguration? Configuration { get; init; }
	public List<string> Warnings { get; init; } = [];
	public List<string> Errors { get; init; } = [];

	// NEW LINES:
	public OperationExecutionGraph? ExecutionGraph { get; init; }
	public OperationTrackingReport? TrackingReport { get; init; }
}
```

**Checklist**:
- [ ] File updated with 2 new properties
- [ ] Both properties are nullable (?)
- [ ] No change to existing properties
- [ ] Compiles without errors

---

## Phase 2: Create Interface & Service

### Task 2.1: Create IOperationExecutionGraphBuilder.cs

**File**: `src/TerraformApi.Domain/Interfaces/IOperationExecutionGraphBuilder.cs`

```
✅ Copy from OPERATION_TRACKING_IMPLEMENTATION.md
✅ 6 methods + 1 enum
✅ All using()  statements included
```

**Checklist**:
- [ ] File created in correct folder
- [ ] Namespace: `TerraformApi.Domain.Interfaces`
- [ ] Using statements: `TerraformApi.Domain.Models`
- [ ] 6 methods defined
- [ ] VisualizationFormat enum with 5 values
- [ ] Compiles without errors

### Task 2.2: Create OperationExecutionGraphBuilderService.cs

**File**: `src/TerraformApi.Application/Services/OperationExecutionGraphBuilderService.cs`

```
✅ Copy from OPERATION_TRACKING_IMPLEMENTATION.md
✅ Implement IOperationExecutionGraphBuilder
✅ All methods have at least skeleton implementation
```

**Checklist**:
- [ ] File created in correct folder
- [ ] Namespace: `TerraformApi.Application.Services`
- [ ] Class sealed, implements IOperationExecutionGraphBuilder
- [ ] Method 1: BuildFromConfiguration() ✓
- [ ] Method 2: BuildMergedGraph() ✓
- [ ] Method 3: AnalyzeDependencies() ✓
- [ ] Method 4: ValidateExecutionSequence() ✓
- [ ] Method 5: ExportToVisualization() ✓
- [ ] Method 6: GenerateTrackingReport() ✓
- [ ] Helper methods included:
  - [ ] UpdateStatistics()
  - [ ] ExtractOperationsFromTerraform()
  - [ ] GroupOperationsByStatus()
  - [ ] GroupOperationsBySource()
  - [ ] DetectDependencies()
  - [ ] ExportToJson()
  - [ ] ExportToMermaid() [Optional: skip for Option B/C]
  - [ ] ExportToCsv() [Optional: skip for Option B/C]
- [ ] Compiles without errors

---

## Phase 3: Register & Integrate

### Task 3.1: Update DependencyInjection.cs

**File**: `src/TerraformApi.Application/DependencyInjection.cs`

```
BEFORE:
-------
public static IServiceCollection AddApplicationServices(this IServiceCollection services)
{
	services.AddSingleton<IApimNamingValidator, ApimNamingValidatorService>();
	services.AddSingleton<IOpenApiParser, OpenApiParserService>();
	services.AddSingleton<ITerraformGenerator, TerraformGeneratorService>();
	services.AddSingleton<ITerraformMerger, TerraformMergerService>();
	services.AddSingleton<IConversionOrchestrator, ConversionOrchestratorService>();
	services.AddSingleton<IEnvironmentTransformer, EnvironmentTransformerService>();
	return services;
}

AFTER (ADD ONE LINE):
-----
public static IServiceCollection AddApplicationServices(this IServiceCollection services)
{
	services.AddSingleton<IApimNamingValidator, ApimNamingValidatorService>();
	services.AddSingleton<IOpenApiParser, OpenApiParserService>();
	services.AddSingleton<ITerraformGenerator, TerraformGeneratorService>();
	services.AddSingleton<ITerraformMerger, TerraformMergerService>();
	services.AddSingleton<IConversionOrchestrator, ConversionOrchestratorService>();
	services.AddSingleton<IEnvironmentTransformer, EnvironmentTransformerService>();
	services.AddSingleton<IOperationExecutionGraphBuilder, OperationExecutionGraphBuilderService>(); // ADD THIS
	return services;
}
```

**Checklist**:
- [ ] File opened
- [ ] One line added
- [ ] Using statement imports not needed (should already have them)
- [ ] Compiles without errors

### Task 3.2: Update ConversionOrchestratorService.cs

**File**: `src/TerraformApi.Application/Services/ConversionOrchestratorService.cs`

#### Step 1: Add field and update constructor

```
BEFORE:
-------
private readonly IOpenApiParser _parser;
private readonly ITerraformGenerator _generator;
private readonly ITerraformMerger _merger;
private readonly IApimNamingValidator _namingValidator;

public ConversionOrchestratorService(
	IOpenApiParser parser,
	ITerraformGenerator generator,
	ITerraformMerger merger,
	IApimNamingValidator namingValidator)
{
	_parser = parser;
	_generator = generator;
	_merger = merger;
	_namingValidator = namingValidator;
}

AFTER:
------
private readonly IOpenApiParser _parser;
private readonly ITerraformGenerator _generator;
private readonly ITerraformMerger _merger;
private readonly IApimNamingValidator _namingValidator;
private readonly IOperationExecutionGraphBuilder _graphBuilder; // ADD

public ConversionOrchestratorService(
	IOpenApiParser parser,
	ITerraformGenerator generator,
	ITerraformMerger merger,
	IApimNamingValidator namingValidator,
	IOperationExecutionGraphBuilder graphBuilder) // ADD PARAM
{
	_parser = parser;
	_generator = generator;
	_merger = merger;
	_namingValidator = namingValidator;
	_graphBuilder = graphBuilder; // ADD
}
```

**Checklist**:
- [ ] Private field added
- [ ] Constructor parameter added
- [ ] Assignment in constructor body added
- [ ] Compiles without errors

#### Step 2: Update Convert method

Find the `Convert` method and add this code after `configuration` is created:

```csharp
// In Convert method, after:
// var configuration = _parser.Parse(openApiJson, settings);
//
// ADD THESE LINES:

// Build execution graph
var graph = _graphBuilder.BuildFromConfiguration(configuration);
_graphBuilder.AnalyzeDependencies(graph);
var graphIssues = _graphBuilder.ValidateExecutionSequence(graph);
var trackingReport = _graphBuilder.GenerateTrackingReport(graph);

// Add graph issues to warnings
warnings.AddRange(graphIssues.Select(i => i.Message));
```

Then find where `ConversionResult` is returned and update it:

```csharp
BEFORE:
-------
return new ConversionResult
{
	Success = true,
	TerraformConfig = terraform,
	Configuration = configuration,
	Warnings = warnings
};

AFTER:
------
return new ConversionResult
{
	Success = true,
	TerraformConfig = terraform,
	Configuration = configuration,
	ExecutionGraph = graph,          // ADD
	TrackingReport = trackingReport, // ADD
	Warnings = warnings
};
```

**Checklist**:
- [ ] Graph builder calls added (4 lines)
- [ ] Graph issues added to warnings
- [ ] ExecutionGraph property set
- [ ] TrackingReport property set
- [ ] Compiles without errors

---

## Phase 4: Build & Test

### Task 4.1: Build Solution

```
Command: dotnet build
Expected: ✅ Build successful (0 errors, 0 warnings)
```

**Checklist**:
- [ ] Run: `dotnet build` in terminal
- [ ] Check output: "Build succeeded"
- [ ] 0 errors
- [ ] 0 warnings (or acceptable existing warnings)

### Task 4.2: Quick Manual Test

Create a simple test in a unit test file or temporary main:

```csharp
// Create minimal test
var result = orchestrator.Convert(simpleOpenApiJson, new ConversionSettings { /* ... */ });

// Verify graph exists
Assert.NotNull(result.ExecutionGraph);
Assert.NotNull(result.TrackingReport);

// Verify statistics
Assert.True(result.ExecutionGraph.Statistics.TotalOperations > 0);

// Print for visual inspection
Console.WriteLine($"Total operations: {result.ExecutionGraph.Statistics.TotalOperations}");
foreach (var op in result.ExecutionGraph.Nodes.Values)
{
	Console.WriteLine($"  {op.OperationId}: {op.Status}");
}
```

**Checklist**:
- [ ] Test compiles
- [ ] Test runs without exceptions
- [ ] ExecutionGraph is not null
- [ ] TrackingReport is not null
- [ ] Statistics are calculated
- [ ] At least one operation appears in graph

### Task 4.3: Existing Tests Still Pass

```
Command: dotnet test
Expected: ✅ All tests pass (no new failures)
```

**Checklist**:
- [ ] Run: `dotnet test` in terminal
- [ ] Check output: All tests pass
- [ ] No new failures introduced
- [ ] Existing functionality unchanged

---

## Optional: Phase 5 - Visualizations

**Only do this if you chose Option A or want enhanced output**

### Task 5.1: Test Mermaid Export

```csharp
var diagram = graphBuilder.ExportToVisualization(
	result.ExecutionGraph,
	VisualizationFormat.Mermaid);

Console.WriteLine(diagram);
// Output should look like:
// ```mermaid
// graph TD
//     ...
// ```
```

**Checklist**:
- [ ] Compiles
- [ ] Outputs valid Mermaid syntax
- [ ] Can paste output to https://mermaid.live and render
- [ ] Only if you implemented ExportToMermaid()

### Task 5.2: Test JSON Export

```csharp
var json = graphBuilder.ExportToVisualization(
	result.ExecutionGraph,
	VisualizationFormat.Json);

Console.WriteLine(json);
// Should be valid JSON
```

**Checklist**:
- [ ] Valid JSON output
- [ ] Contains all operations
- [ ] Can parse with JsonDocument.Parse()
- [ ] Only if you implemented ExportToJson()

---

## Verification Checklist - Full

### Files Created
- [ ] `src/TerraformApi.Domain/Models/OperationExecutionGraph.cs`
- [ ] `src/TerraformApi.Domain/Models/OperationTrackingReport.cs`
- [ ] `src/TerraformApi.Domain/Interfaces/IOperationExecutionGraphBuilder.cs`
- [ ] `src/TerraformApi.Application/Services/OperationExecutionGraphBuilderService.cs`

### Files Modified
- [ ] `src/TerraformApi.Domain/Models/ConversionResult.cs` (2 lines added)
- [ ] `src/TerraformApi.Application/DependencyInjection.cs` (1 line added)
- [ ] `src/TerraformApi.Application/Services/ConversionOrchestratorService.cs` (1 field + 1 constructor param + 4 method lines + 2 return property lines)

### Compilation
- [ ] Solution builds (0 errors)
- [ ] No warnings (or only pre-existing)

### Functionality
- [ ] ExecutionGraph created correctly
- [ ] Statistics calculated accurately
- [ ] Operations added to Nodes
- [ ] Graph integrates with orchestrator
- [ ] Result contains ExecutionGraph and TrackingReport
- [ ] Existing tests still pass
- [ ] New functionality doesn't break old code

### Documentation
- [ ] OPERATION_TRACKING_ANALYSIS.md created ✓
- [ ] OPERATION_TRACKING_VISUALS.md created ✓
- [ ] OPERATION_TRACKING_IMPLEMENTATION.md created ✓
- [ ] EXECUTIVE_SUMMARY.md created ✓
- [ ] QUICK_REFERENCE.md created ✓
- [ ] This file (IMPLEMENTATION_ROADMAP.md) created ✓

---

## Timeline Estimates

```
Task 1.1 (Create OperationExecutionGraph.cs):     15 mins
Task 1.2 (Create OperationTrackingReport.cs):     10 mins
Task 1.3 (Update ConversionResult.cs):             5 mins
Task 2.1 (Create Interface):                       10 mins
Task 2.2 (Create Service):                         30 mins
Task 3.1 (Update DependencyInjection):              5 mins
Task 3.2 (Update ConversionOrchestrator):         20 mins
Task 4.1 (Build):                                   5 mins
Task 4.2 (Manual Test):                           15 mins
Task 4.3 (Run Tests):                             10 mins
─────────────────────────────────────────────────────────
TOTAL (Core without visualizations): ~2 hours 25 mins
TOTAL (With visualizations): ~3 hours
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Can't find IOperationExecutionGraphBuilder | Check: registered in DI + correct interface file location |
| Statistics are wrong | Check: UpdateStatistics() called + Nodes being populated correctly |
| Graph is always empty | Check: BuildFromConfiguration() iterating through ApiOperations |
| Compilation error: CS0103 "name does not exist" | Check: Missing using statements or typo in interface/class name |
| Existing tests fail | Check: No breaking changes to existing classes, only additive |

---

## Success Milestones

```
Milestone 1 ✅: Models compile
Milestone 2 ✅: Service compiles
Milestone 3 ✅: DI updated and compiles
Milestone 4 ✅: Orchestrator updated and compiles
Milestone 5 ✅: Solution builds (no errors)
Milestone 6 ✅: Existing tests pass
Milestone 7 ✅: New functionality works (graph created)
Milestone 8 ✅: Statistics calculate correctly
Milestone 9 ✅: Visualizations generate (if included)
Milestone 10 ✅: 🎉 System working! Ready for production
```

---

## After Implementation

### Use It
1. Convert OpenAPI to Terraform and get graph
2. Export visualizations for team review
3. Check operation status and deltas
4. Preserve custom operations safely

### Enhance It (Future)
- [ ] Add more sophisticated dependency detection
- [ ] Implement policy tracking
- [ ] Add cost/impact analysis
- [ ] Create UI dashboard showing graphs
- [ ] Add versioning/history tracking
- [ ] Implement approval workflows

### Share It
- Document the graph format
- Provide visualization examples
- Show merge scenarios
- Create team guidelines

---

**Ready to start? Begin with Phase 1, Task 1.1! ✨**
