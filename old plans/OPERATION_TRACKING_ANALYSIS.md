# Operation Tracking & Execution Graph System - Analysis & Plan

## Current State Analysis

### Architecture Overview
```
OpenAPI JSON
	↓
ConversionOrchestratorService
	├─ OpenApiParserService (Parse → ApimConfiguration)
	│   └─ Returns: ApimConfiguration with ApiOperations[]
	├─ TerraformGeneratorService (Generate HCL)
	│   └─ Returns: String (HCL)
	├─ TerraformMergerService (Merge with existing)
	│   └─ Preserves custom operations by operation_id matching
	└─ ApimNamingValidatorService (Validate names)
		└─ Returns: Warnings[]

ConversionResult (Success, TerraformConfig, Configuration, Warnings, Errors)
```

### Current Operation Tracking
- **Minimal tracking**: Operation IDs are extracted from HCL using regex in TerraformMergerService
- **No execution graph**: No visibility into which methods should proceed, which are blocked, dependencies
- **No state machine**: No concept of operation status (pending, processed, blocked, skipped)
- **No audit trail**: No tracking of why an operation was/wasn't included

### Key Problem
When you have a pre-existing Terraform APIM configuration:
1. You want to **preserve** existing operations
2. You want to **track** which OpenAPI operations map to existing Terraform operations
3. You want to **know** which operations are new, which are deleted, which have parameter changes
4. You have **no way to visualize** the dependency/execution flow

## What Needs to Change

### 1. **New Domain Models** (TerraformApi.Domain/Models/)

#### OperationExecutionGraph.cs
```csharp
public sealed record OperationExecutionNode
{
	public required string OperationId { get; init; }
	public required string Method { get; init; }  // GET, POST, etc.
	public required string UrlTemplate { get; init; }
	public OperationStatus Status { get; init; } = OperationStatus.Pending;
	public OperationSource Source { get; init; }  // OpenApi, ExistingConfig, Manual
	public List<string> DependsOnOperationIds { get; init; } = [];
	public List<string> ReferencedBy { get; init; } = [];
	public string? Reason { get; init; }  // Why included/excluded
	public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public enum OperationStatus
{
	Pending,           // Not yet processed
	Included,          // Will be in output
	Excluded,          // Removed from previous config
	Modified,          // Changed from previous config
	Skipped,           // Intentionally skipped
	Blocked,           // Cannot be processed
	Deprecated         // Marked for removal
}

public enum OperationSource
{
	OpenApi,           // From new OpenAPI spec
	ExistingConfig,    // From existing Terraform config
	Custom,            // Manually added
	Generated          // Auto-generated
}

public sealed record OperationExecutionGraph
{
	public required string ApiGroupName { get; init; }
	public Dictionary<string, OperationExecutionNode> Nodes { get; init; } = [];
	public List<ExecutionGraphIssue> Issues { get; init; } = [];
	public ExecutionGraphStatistics Statistics { get; init; } = new();
	public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed record ExecutionGraphIssue
{
	public string? OperationId { get; init; }
	public IssueLevel Level { get; init; }  // Info, Warning, Error
	public string Message { get; init; }
	public string? Recommendation { get; init; }
}

public enum IssueLevel { Info, Warning, Error }

public sealed record ExecutionGraphStatistics
{
	public int TotalOperations { get; init; }
	public int IncludedOperations { get; init; }
	public int ExcludedOperations { get; init; }
	public int ModifiedOperations { get; init; }
	public int BlockedOperations { get; init; }
	public int NewOperations { get; init; }
	public int DeletedOperations { get; init; }
	public double CompletionPercentage { get; init; }
}
```

### 2. **New Interface** (TerraformApi.Domain/Interfaces/)

#### IOperationExecutionGraphBuilder.cs
```csharp
public interface IOperationExecutionGraphBuilder
{
	/// Build graph from new OpenAPI config
	OperationExecutionGraph BuildFromConfiguration(ApimConfiguration config);

	/// Build graph comparing existing HCL with new config
	OperationExecutionGraph BuildMergedGraph(
		string existingTerraform,
		ApimConfiguration newConfiguration);

	/// Detect dependencies between operations
	void AnalyzeDependencies(OperationExecutionGraph graph);

	/// Validate execution sequence
	List<ExecutionGraphIssue> ValidateExecutionSequence(OperationExecutionGraph graph);

	/// Export graph to visualization (JSON, Mermaid, PlantUML)
	string ExportToVisualization(OperationExecutionGraph graph, VisualizationFormat format);
}

public enum VisualizationFormat
{
	Json,
	Mermaid,      // ```mermaid graph TD
	PlantUML,     // @startuml
	Graphviz      // dot format
}
```

### 3. **New Service** (TerraformApi.Application/Services/)

#### OperationExecutionGraphBuilderService.cs
- Tracks each operation: status, source, reason
- Detects operations that are new, modified, deleted, preserved
- Analyzes dependencies (e.g., Operation A calls Operation B)
- Identifies blocking issues
- Supports visualization export

### 4. **Enhanced ConversionResult**

Update `ConversionResult.cs`:
```csharp
public sealed class ConversionResult
{
	public bool Success { get; init; }
	public string TerraformConfig { get; init; } = "";
	public ApimConfiguration? Configuration { get; init; }
	public List<string> Warnings { get; init; } = [];
	public List<string> Errors { get; init; } = [];

	// NEW: Execution tracking
	public OperationExecutionGraph? ExecutionGraph { get; init; }
	public OperationTrackingReport? TrackingReport { get; init; }
}
```

#### OperationTrackingReport.cs (NEW)
```csharp
public sealed record OperationTrackingReport
{
	public int TotalOperationsTracked { get; init; }
	public Dictionary<OperationStatus, List<string>> OperationsByStatus { get; init; } = [];
	public Dictionary<OperationSource, List<string>> OperationsBySource { get; init; } = [];
	public List<OperationDelta> Deltas { get; init; } = [];  // New/Modified/Deleted
	public List<OperationDependency> Dependencies { get; init; } = [];
}

public sealed record OperationDelta
{
	public required string OperationId { get; init; }
	public OperationChangeType ChangeType { get; init; }
	public string? FromValue { get; init; }
	public string? ToValue { get; init; }
	public string? Reason { get; init; }
}

public enum OperationChangeType
{
	Added,
	Removed,
	MethodChanged,
	UrlTemplateChanged,
	ParametersChanged,
	StatusCodeChanged
}

public sealed record OperationDependency
{
	public required string SourceOperationId { get; init; }
	public required string TargetOperationId { get; init; }
	public DependencyType Type { get; init; }
}

public enum DependencyType
{
	Calls,           // Operation A calls Operation B
	Consumes,        // Operation A uses output of B
	Replaces,        // Operation A replaces/supersedes B
	RelatedTo        // Semantically related
}
```

### 5. **Enhanced ConversionOrchestrator**

Update pipeline to:
```csharp
public ConversionResult Convert(string openApiJson, ConversionSettings settings)
{
	var configuration = _parser.Parse(openApiJson, settings);

	// NEW: Build execution graph
	var executionGraph = _graphBuilder.BuildFromConfiguration(configuration);
	_graphBuilder.AnalyzeDependencies(executionGraph);
	var graphIssues = _graphBuilder.ValidateExecutionSequence(executionGraph);

	var terraform = _generator.Generate(configuration);

	var trackingReport = _reportGenerator.GenerateReport(executionGraph);

	return new ConversionResult
	{
		Success = true,
		TerraformConfig = terraform,
		Configuration = configuration,
		ExecutionGraph = executionGraph,  // NEW
		TrackingReport = trackingReport,   // NEW
		Warnings = warnings.Concat(graphIssues).ToList()
	};
}
```

### 6. **Enhanced TerraformMerger**

Track operation changes:
```csharp
public (string MergedTerraform, OperationExecutionGraph Graph) MergeWithTracking(
	string existingTerraform,
	ApimConfiguration newConfiguration)
{
	// Existing merge logic...
	var merged = _generator.Generate(newConfiguration);

	// NEW: Build execution graph showing before/after
	var graph = _graphBuilder.BuildMergedGraph(existingTerraform, newConfiguration);

	return (merged, graph);
}
```

## Implementation Timeline

### Phase 1: Foundation (Low effort, high value)
1. Create OpeationExecutionNode and OperationStatus enum
2. Create OperationExecutionGraph model
3. Create IOperationExecutionGraphBuilder interface
4. Update ConversionResult with new properties

### Phase 2: Core Logic (Medium effort)
1. Implement OperationExecutionGraphBuilderService
   - Parse operation IDs from existing Terraform
   - Compare with new config
   - Assign status and source
   - Generate deltas
2. Implement dependency analysis
3. Update ConversionOrchestratorService to use graph builder

### Phase 3: Visualization & Export (Medium effort)
1. Implement graph export to JSON
2. Implement Mermaid diagram generation
3. Implement PlantUML export
4. Create OperationTrackingReport generation

### Phase 4: API & UI Integration (Optional)
1. Add endpoints to retrieve operation graph
2. Add endpoints to export visualizations
3. Update frontend to display graph/report

## Usage Examples

### Get Operation Status
```csharp
var result = orchestrator.Convert(openApiJson, settings);

foreach(var node in result.ExecutionGraph.Nodes.Values)
{
	Console.WriteLine($"{node.OperationId}: {node.Status} (from {node.Source})");
	if (node.Status == OperationStatus.Blocked)
		Console.WriteLine($"  Reason: {node.Reason}");
}
```

### Visualize Operation Flow
```csharp
var mermaidDiagram = graphBuilder.ExportToVisualization(
	executionGraph,
	VisualizationFormat.Mermaid);
// Output: ```mermaid
//         graph TD
//           GET_Users["GET /users"]
//           GET_UserId["GET /users/{id}"]
//           PUT_UserId["PUT /users/{id}"]
//           GET_Users -->|calls| GET_UserId
//           GET_UserId -->|replaced by| PUT_UserId

### Track Changes
```csharp
var report = result.TrackingReport;

Console.WriteLine($"New operations: {report.Deltas.Count(d => d.ChangeType == OperationChangeType.Added)}");
Console.WriteLine($"Removed operations: {report.Deltas.Count(d => d.ChangeType == OperationChangeType.Removed)}");
Console.WriteLine($"Modified operations: {report.Deltas.Count(d => d.ChangeType.ToString().Contains("Changed"))}");
```

## Benefits

1. **Visibility**: See exactly which operations will be processed and why
2. **Debugging**: Easy to identify blocking issues and dependencies
3. **Merge Safety**: Understand what's being preserved vs. replaced
4. **Compliance**: Audit trail showing all operation changes
5. **Visualization**: Export as diagrams for documentation/review
6. **Extensibility**: Easy to add new tracking rules and validations

## Testing Opportunities

1. Unit tests for graph building with various Delta scenarios
2. Integration tests for merge tracking
3. Edge case tests: circular dependencies, orphaned operations
4. Visualization export correctness tests
5. Performance tests with large operation counts (100+)

## Notes

- This system is **non-invasive**: existing code continues to work
- Graph building is **optional**: can be added to pipeline without breaking changes
- Design supports **future enhancements**: policies, resource tracking, cost analysis
- Integrates with **existing Terraform operations**: doesn't change how HCL is generated
