# Operation Tracking System - Implementation Guide

## Quick Start: What to Build

### Step 1: Create Domain Models (Copy-Paste Ready)

**File**: `src/TerraformApi.Domain/Models/OperationExecutionGraph.cs`

```csharp
namespace TerraformApi.Domain.Models;

public sealed record OperationExecutionNode
{
	/// Unique identifier matching ApimApiOperation.OperationId
	public required string OperationId { get; init; }

	/// HTTP method (GET, POST, PUT, DELETE, PATCH)
	public required string Method { get; init; }

	/// Terraform URL template (e.g., "/users/{id}")
	public required string UrlTemplate { get; init; }

	/// Current status in execution pipeline
	public OperationStatus Status { get; init; } = OperationStatus.Pending;

	/// Where this operation came from
	public OperationSource Source { get; init; } = OperationSource.OpenApi;

	/// Operation IDs that this operation depends on
	public List<string> DependsOnOperationIds { get; init; } = [];

	/// Operation IDs that reference this operation
	public List<string> ReferencedBy { get; init; } = [];

	/// Human-readable reason for status/inclusion
	public string? Reason { get; init; }

	/// Metadata tags for filtering/grouping
	public Dictionary<string, string> Tags { get; init; } = [];

	/// When this node was created
	public DateTime TrackedAt { get; init; } = DateTime.UtcNow;
}

public enum OperationStatus
{
	/// Not yet evaluated
	Pending = 0,

	/// Will be included in output
	Included = 1,

	/// Removed from previous configuration
	Excluded = 2,

	/// Changed from previous version
	Modified = 3,

	/// Intentionally skipped
	Skipped = 4,

	/// Cannot be processed due to errors
	Blocked = 5,

	/// Marked for deprecation
	Deprecated = 6
}

public enum OperationSource
{
	/// From the new OpenAPI specification
	OpenApi = 0,

	/// From existing Terraform configuration
	ExistingConfig = 1,

	/// Manually added by user
	Custom = 2,

	/// Auto-generated during processing
	Generated = 3
}

public sealed record OperationExecutionGraph
{
	/// Name of the API group from configuration
	public required string ApiGroupName { get; init; }

	/// All tracked operations keyed by OperationId
	public Dictionary<string, OperationExecutionNode> Nodes { get; init; } = [];

	/// Issues encountered during graph building/validation
	public List<ExecutionGraphIssue> Issues { get; init; } = [];

	/// Summary statistics
	public ExecutionGraphStatistics Statistics { get; init; } = new();

	/// When graph was generated
	public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public sealed record ExecutionGraphIssue
{
	/// Operation ID if issue is specific to an operation (null if global)
	public string? OperationId { get; init; }

	/// Severity of issue
	public IssueLevel Level { get; init; }

	/// What went wrong
	public required string Message { get; init; }

	/// Suggested fix
	public string? Recommendation { get; init; }
}

public enum IssueLevel
{
	/// Informational message
	Info = 0,

	/// Warning - may cause unexpected behavior
	Warning = 1,

	/// Error - operation cannot be processed
	Error = 2
}

public sealed record ExecutionGraphStatistics
{
	public int TotalOperations { get; init; }
	public int IncludedOperations { get; init; }
	public int ExcludedOperations { get; init; }
	public int ModifiedOperations { get; init; }
	public int BlockedOperations { get; init; }
	public int SkippedOperations { get; init; }
	public int NewOperations { get; init; }
	public int DeletedOperations { get; init; }

	/// Percentage of operations that will be processed (included + modified)
	public double CompletionPercentage
	{
		get
		{
			if (TotalOperations == 0) return 0;
			var processed = IncludedOperations + ModifiedOperations;
			return (processed / (double)TotalOperations) * 100;
		}
	}
}
```

**File**: `src/TerraformApi.Domain/Models/OperationTrackingReport.cs`

```csharp
namespace TerraformApi.Domain.Models;

/// Summary report of operation changes and tracking info
public sealed record OperationTrackingReport
{
	/// Total number of operations tracked
	public int TotalOperationsTracked { get; init; }

	/// Operations grouped by their status
	public Dictionary<OperationStatus, List<string>> OperationsByStatus { get; init; } = [];

	/// Operations grouped by their source
	public Dictionary<OperationSource, List<string>> OperationsBySource { get; init; } = [];

	/// All detected changes (new/removed/modified)
	public List<OperationDelta> Deltas { get; init; } = [];

	/// Detected dependencies between operations
	public List<OperationDependency> Dependencies { get; init; } = [];

	/// When report was generated
	public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// Represents a change to an operation
public sealed record OperationDelta
{
	/// Operation identifier
	public required string OperationId { get; init; }

	/// What changed
	public OperationChangeType ChangeType { get; init; }

	/// Previous value (before change)
	public string? FromValue { get; init; }

	/// New value (after change)
	public string? ToValue { get; init; }

	/// Why the change occurred
	public string? Reason { get; init; }
}

public enum OperationChangeType
{
	/// New operation added
	Added = 0,

	/// Operation removed
	Removed = 1,

	/// HTTP method changed (GET → POST, etc.)
	MethodChanged = 2,

	/// URL template changed
	UrlTemplateChanged = 3,

	/// Parameters added/removed/modified
	ParametersChanged = 4,

	/// Response status code changed
	StatusCodeChanged = 5,

	/// Description/displayname changed
	DescriptionChanged = 6,

	/// Multiple changes
	Multiple = 7
}

/// Represents relationship between two operations
public sealed record OperationDependency
{
	/// Source operation ID
	public required string SourceOperationId { get; init; }

	/// Target/referenced operation ID
	public required string TargetOperationId { get; init; }

	/// Type of relationship
	public DependencyType Type { get; init; }

	/// Direction confidence (0-1): how certain is this dependency
	public double Confidence { get; init; } = 1.0;

	/// Human-readable description
	public string? Description { get; init; }
}

public enum DependencyType
{
	/// Operation A calls Operation B
	Calls = 0,

	/// Operation A consumes output of Operation B
	Consumes = 1,

	/// Operation A replaces/supersedes Operation B
	Replaces = 2,

	/// Operations are related but not direct dependency
	RelatedTo = 3,

	/// Operation A must run before Operation B
	PreconditionFor = 4
}
```

### Step 2: Update ConversionResult

**File**: `src/TerraformApi.Domain/Models/ConversionResult.cs`

```csharp
namespace TerraformApi.Domain.Models;

public sealed class ConversionResult
{
	public bool Success { get; init; }
	public string TerraformConfig { get; init; } = "";
	public ApimConfiguration? Configuration { get; init; }
	public List<string> Warnings { get; init; } = [];
	public List<string> Errors { get; init; } = [];

	// NEW: Operation tracking data
	public OperationExecutionGraph? ExecutionGraph { get; init; }
	public OperationTrackingReport? TrackingReport { get; init; }
}
```

### Step 3: Create Interface

**File**: `src/TerraformApi.Domain/Interfaces/IOperationExecutionGraphBuilder.cs`

```csharp
using TerraformApi.Domain.Models;

namespace TerraformApi.Domain.Interfaces;

public interface IOperationExecutionGraphBuilder
{
	/// Build a graph from new OpenAPI-derived configuration
	OperationExecutionGraph BuildFromConfiguration(ApimConfiguration config);

	/// Build a merged graph comparing existing Terraform with new config
	OperationExecutionGraph BuildMergedGraph(
		string existingTerraform,
		ApimConfiguration newConfiguration);

	/// Analyze and populate dependency relationships in the graph
	void AnalyzeDependencies(OperationExecutionGraph graph);

	/// Validate the execution sequence and return any issues found
	List<ExecutionGraphIssue> ValidateExecutionSequence(OperationExecutionGraph graph);

	/// Export graph to a visualization format
	string ExportToVisualization(
		OperationExecutionGraph graph,
		VisualizationFormat format);

	/// Generate a tracking report from the graph
	OperationTrackingReport GenerateTrackingReport(OperationExecutionGraph graph);
}

public enum VisualizationFormat
{
	/// JSON format for API responses
	Json = 0,

	/// Mermaid diagram syntax for markdown rendering
	Mermaid = 1,

	/// PlantUML diagram syntax
	PlantUML = 2,

	/// Graphviz DOT format
	Graphviz = 3,

	/// CSV format for Excel/sheets
	Csv = 4
}
```

### Step 4: Implement Service (Skeleton)

**File**: `src/TerraformApi.Application/Services/OperationExecutionGraphBuilderService.cs`

```csharp
using System.Text;
using System.Text.RegularExpressions;
using TerraformApi.Domain.Interfaces;
using TerraformApi.Domain.Models;

namespace TerraformApi.Application.Services;

public sealed class OperationExecutionGraphBuilderService : IOperationExecutionGraphBuilder
{
	public OperationExecutionGraph BuildFromConfiguration(ApimConfiguration config)
	{
		var graph = new OperationExecutionGraph
		{
			ApiGroupName = config.ApiGroupName
		};

		// Add each operation as a node with Included status
		foreach (var operation in config.ApiOperations)
		{
			graph.Nodes[operation.OperationId] = new OperationExecutionNode
			{
				OperationId = operation.OperationId,
				Method = operation.Method,
				UrlTemplate = operation.UrlTemplate,
				Status = OperationStatus.Included,
				Source = OperationSource.OpenApi,
				Reason = "Operation from new OpenAPI specification"
			};
		}

		// Update statistics
		UpdateStatistics(graph);

		return graph;
	}

	public OperationExecutionGraph BuildMergedGraph(
		string existingTerraform,
		ApimConfiguration newConfiguration)
	{
		// Extract existing operation IDs from HCL
		var existingOperations = ExtractOperationsFromTerraform(existingTerraform);
		var newOperationIds = newConfiguration.ApiOperations
			.Select(op => op.OperationId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		var graph = new OperationExecutionGraph
		{
			ApiGroupName = newConfiguration.ApiGroupName
		};

		// Add new operations
		foreach (var operation in newConfiguration.ApiOperations)
		{
			var isModified = existingOperations.ContainsKey(operation.OperationId);

			graph.Nodes[operation.OperationId] = new OperationExecutionNode
			{
				OperationId = operation.OperationId,
				Method = operation.Method,
				UrlTemplate = operation.UrlTemplate,
				Status = isModified ? OperationStatus.Modified : OperationStatus.Included,
				Source = isModified ? OperationSource.ExistingConfig : OperationSource.OpenApi,
				Reason = isModified 
					? "Operation exists in both specs, may have changes"
					: "New operation from OpenAPI specification"
			};
		}

		// Add excluded operations (in existing but not in new)
		foreach (var (operationId, operation) in existingOperations)
		{
			if (!newOperationIds.Contains(operationId))
			{
				graph.Nodes[operationId] = new OperationExecutionNode
				{
					OperationId = operationId,
					Method = operation.Method,
					UrlTemplate = operation.UrlTemplate,
					Status = OperationStatus.Excluded,
					Source = OperationSource.ExistingConfig,
					Reason = "Operation not in new OpenAPI spec (preserved in merge)"
				};
			}
		}

		UpdateStatistics(graph);
		return graph;
	}

	public void AnalyzeDependencies(OperationExecutionGraph graph)
	{
		// TODO: Implement dependency detection logic
		// Examples:
		// - GET /users likely depends on nothing
		// - PUT /users/{id} likely depends on GET /users/{id}
		// - DELETE /users likely depends on GET /users
	}

	public List<ExecutionGraphIssue> ValidateExecutionSequence(OperationExecutionGraph graph)
	{
		var issues = new List<ExecutionGraphIssue>();

		// Check for blocked operations
		var blocked = graph.Nodes.Values
			.Where(n => n.Status == OperationStatus.Blocked)
			.ToList();

		if (blocked.Any())
		{
			issues.Add(new ExecutionGraphIssue
			{
				Level = IssueLevel.Error,
				Message = $"Found {blocked.Count} blocked operations",
				Recommendation = "Review blocked operation logs for details"
			});
		}

		return issues;
	}

	public string ExportToVisualization(
		OperationExecutionGraph graph,
		VisualizationFormat format)
	{
		return format switch
		{
			VisualizationFormat.Mermaid => ExportToMermaid(graph),
			VisualizationFormat.Json => ExportToJson(graph),
			VisualizationFormat.Csv => ExportToCsv(graph),
			_ => throw new NotSupportedException($"Format {format} not yet implemented")
		};
	}

	public OperationTrackingReport GenerateTrackingReport(OperationExecutionGraph graph)
	{
		var report = new OperationTrackingReport
		{
			TotalOperationsTracked = graph.Nodes.Count,
			OperationsByStatus = GroupOperationsByStatus(graph),
			OperationsBySource = GroupOperationsBySource(graph),
			Deltas = [],  // Will be populated when comparing versions
			Dependencies = DetectDependencies(graph)
		};

		return report;
	}

	// Helper methods

	private void UpdateStatistics(OperationExecutionGraph graph)
	{
		var stats = new ExecutionGraphStatistics
		{
			TotalOperations = graph.Nodes.Count,
			IncludedOperations = graph.Nodes.Values.Count(n => n.Status == OperationStatus.Included),
			ExcludedOperations = graph.Nodes.Values.Count(n => n.Status == OperationStatus.Excluded),
			ModifiedOperations = graph.Nodes.Values.Count(n => n.Status == OperationStatus.Modified),
			BlockedOperations = graph.Nodes.Values.Count(n => n.Status == OperationStatus.Blocked),
			SkippedOperations = graph.Nodes.Values.Count(n => n.Status == OperationStatus.Skipped)
		};

		graph.Statistics = stats;
	}

	private Dictionary<string, (string Method, string UrlTemplate)> ExtractOperationsFromTerraform(string terraform)
	{
		var operations = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

		// Regex to find operation_id = "..." and method = "..." and url_template = "..."
		var operationIdRegex = new Regex(@"operation_id\s*=\s*""([^""]+)""");
		var methodRegex = new Regex(@"method\s*=\s*""([^""]+)""");
		var urlRegex = new Regex(@"url_template\s*=\s*""([^""]+)""");

		var operationIdMatches = operationIdRegex.Matches(terraform);
		var methodMatches = methodRegex.Matches(terraform);
		var urlMatches = urlRegex.Matches(terraform);

		// Simplified: assumes operations are in order
		for (int i = 0; i < Math.Min(operationIdMatches.Count, Math.Min(methodMatches.Count, urlMatches.Count)); i++)
		{
			var opId = operationIdMatches[i].Groups[1].Value;
			var method = methodMatches[i].Groups[1].Value;
			var url = urlMatches[i].Groups[1].Value;

			operations[opId] = (method, url);
		}

		return operations;
	}

	private Dictionary<OperationStatus, List<string>> GroupOperationsByStatus(OperationExecutionGraph graph)
	{
		return graph.Nodes.Values
			.GroupBy(n => n.Status)
			.ToDictionary(
				g => g.Key,
				g => g.Select(n => n.OperationId).ToList());
	}

	private Dictionary<OperationSource, List<string>> GroupOperationsBySource(OperationExecutionGraph graph)
	{
		return graph.Nodes.Values
			.GroupBy(n => n.Source)
			.ToDictionary(
				g => g.Key,
				g => g.Select(n => n.OperationId).ToList());
	}

	private List<OperationDependency> DetectDependencies(OperationExecutionGraph graph)
	{
		var dependencies = new List<OperationDependency>();

		// TODO: Implement intelligent dependency detection
		// For now: return empty list

		return dependencies;
	}

	private string ExportToJson(OperationExecutionGraph graph)
	{
		return System.Text.Json.JsonSerializer.Serialize(graph);
	}

	private string ExportToMermaid(OperationExecutionGraph graph)
	{
		var sb = new StringBuilder();
		sb.AppendLine("```mermaid");
		sb.AppendLine("graph TD");

		foreach (var node in graph.Nodes.Values)
		{
			var statusIcon = node.Status switch
			{
				OperationStatus.Included => "✓",
				OperationStatus.Modified => "⚠️",
				OperationStatus.Excluded => "❌",
				OperationStatus.Blocked => "🚫",
				_ => "•"
			};

			var nodeId = node.OperationId.Replace("-", "_").Replace("/", "_");
			sb.AppendLine($"    {nodeId}[\"{statusIcon} {node.Method} {node.UrlTemplate}\"]");

			foreach (var dep in node.DependsOnOperationIds)
			{
				var depId = dep.Replace("-", "_").Replace("/", "_");
				sb.AppendLine($"    {depId} --> {nodeId}");
			}
		}

		sb.AppendLine("```");

		return sb.ToString();
	}

	private string ExportToCsv(OperationExecutionGraph graph)
	{
		var sb = new StringBuilder();
		sb.AppendLine("OperationId,Method,UrlTemplate,Status,Source,Reason");

		foreach (var node in graph.Nodes.Values)
		{
			var reason = (node.Reason ?? "").Replace(",", ";").Replace("\"", "'");
			sb.AppendLine($"\"{node.OperationId}\",\"{node.Method}\",\"{node.UrlTemplate}\",\"{node.Status}\",\"{node.Source}\",\"{reason}\"");
		}

		return sb.ToString();
	}
}
```

### Step 5: Register in DependencyInjection

**File**: `src/TerraformApi.Application/DependencyInjection.cs`

```csharp
services.AddSingleton<IOperationExecutionGraphBuilder, OperationExecutionGraphBuilderService>();
```

### Step 6: Update ConversionOrchestratorService

**File**: `src/TerraformApi.Application/Services/ConversionOrchestratorService.cs`

Add to constructor:
```csharp
private readonly IOperationExecutionGraphBuilder _graphBuilder;

public ConversionOrchestratorService(
	IOpenApiParser parser,
	ITerraformGenerator generator,
	ITerraformMerger merger,
	IApimNamingValidator namingValidator,
	IOperationExecutionGraphBuilder graphBuilder)  // NEW
{
	_parser = parser;
	_generator = generator;
	_merger = merger;
	_namingValidator = namingValidator;
	_graphBuilder = graphBuilder;  // NEW
}
```

Update `Convert` method:
```csharp
public ConversionResult Convert(string openApiJson, ConversionSettings settings)
{
	// ... existing validation logic ...

	var configuration = _parser.Parse(openApiJson, settings);

	// NEW: Build execution graph
	var graph = _graphBuilder.BuildFromConfiguration(configuration);
	_graphBuilder.AnalyzeDependencies(graph);
	var graphIssues = _graphBuilder.ValidateExecutionSequence(graph);
	var trackingReport = _graphBuilder.GenerateTrackingReport(graph);

	warnings.AddRange(ValidateGeneratedNames(configuration));
	warnings.AddRange(graphIssues.Select(i => i.Message));  // Add graph issues to warnings

	var terraform = _generator.Generate(configuration);

	return new ConversionResult
	{
		Success = true,
		TerraformConfig = terraform,
		Configuration = configuration,
		ExecutionGraph = graph,          // NEW
		TrackingReport = trackingReport, // NEW
		Warnings = warnings
	};
}
```

## Testing It

```csharp
var result = orchestrator.Convert(openApiJson, settings);

// Check execution graph
Console.WriteLine($"Total operations tracked: {result.ExecutionGraph?.Statistics.TotalOperations}");
print($"Included: {result.ExecutionGraph?.Statistics.IncludedOperations}");
Console.WriteLine($"Modified: {result.ExecutionGraph?.Statistics.ModifiedOperations}");
Console.WriteLine($"Excluded: {result.ExecutionGraph?.Statistics.ExcludedOperations}");

// Check specific operations
foreach (var node in result.ExecutionGraph?.Nodes.Values ?? [])
{
	Console.WriteLine($"{node.OperationId}: {node.Status} ({node.Source})");
	if (node.Reason != null)
		Console.WriteLine($"  Reason: {node.Reason}");
	if (node.DependsOnOperationIds.Count > 0)
		Console.WriteLine($"  Depends on: {string.Join(", ", node.DependsOnOperationIds)}");
}

// Get visualizations
var mermaidDiagram = graphBuilder.ExportToVisualization(
	result.ExecutionGraph,
	VisualizationFormat.Mermaid);
Console.WriteLine(mermaidDiagram);

var csvReport = graphBuilder.ExportToVisualization(
	result.ExecutionGraph,
	VisualizationFormat.Csv);
File.WriteAllText("operations-report.csv", csvReport);
```

## Next Steps

1. ✅ Create all models (copy-paste the code above)
2. ✅ Create interface
3. ✅ Implement service with skeleton methods
4. ✅ Register in DI
5. ✅ Update ConversionOrchestrator
6. 🔄 Enhance dependency detection logic
7. 🔄 Add change-tracking logic (comparing old vs new)
8. 🔄 Write unit tests
9. 🔄 Create API endpoints to expose graph data
10. 🔄 Add UI visualization (optional)

This gives you **immediate visibility** into which operations are being tracked and why, without breaking any existing functionality.
