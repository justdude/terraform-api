# Operation Tracking System - Visual Architecture

## Current State (Before)
```
┌─────────────────────────────────────────────────────────────────┐
│                         INPUT: OpenAPI JSON                     │
└────────────────────────────┬────────────────────────────────────┘
							 │
							 ▼
┌─────────────────────────────────────────────────────────────────┐
│              ConversionOrchestratorService                      │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────┐  │
│  │ OpenApiParser    │▶ │ TerraformGen     │▶ │ TerraformMgr │  │
│  └──────────────────┘  └──────────────────┘  └──────────────┘  │
│           │                    │                      │         │
│           ▼                    ▼                      ▼         │
│      ApimConfiguration   HCL String         Preserved Ops     │
│        (Operations)                          (Merged)         │
└─────────────────────────────────────────────────────────────────┘
							 │
							 ▼
					┌─────────────────┐
					│ ConversionResult│
					├─────────────────┤
					│ Success: bool   │
					│ Config: ...     │
					│ HCL: string     │◄── No tracking of which
					│ Warnings: [...]│     operations went where
					│ Errors: [...]   │
					└─────────────────┘


## Proposed State (After)
```
┌─────────────────────────────────────────────────────────────────┐
│                    INPUT: OpenAPI + Existing HCL                │
└────────────────────────────┬────────────────────────────────────┘
							 │
					┌────────┴────────┐
					▼                 ▼
		┌──────────────────┐  ┌──────────────────┐
		│  OpenAPI Config  │  │  Existing HCL    │
		│  5 Operations    │  │  8 Operations    │
		└─────────┬────────┘  └────────┬─────────┘
				  │                    │
				  └─────────┬──────────┘
							▼
				┌──────────────────────────┐
				│ OperationExecutionGraph  │
				│   BuilderService (NEW)   │
				└──────────────────────────┘
				  │        │        │
		┌─────────┘        │        └─────────┐
		▼                  ▼                   ▼
   ┌─────────┐        ┌─────────┐        ┌──────────┐
   │ Identify│        │ Analyze │        │Validate  │
   │ Deltas  │        │ Depend. │        │Sequence  │
   └─────────┘        └─────────┘        └──────────┘
		│                  │                   │
		│  ┌───────────────┼───────────────┐   │
		│  │               │               │   │
		└──────────────────┼───────────────┴───┘
						   ▼
		┌────────────────────────────────────┐
		│  OperationExecutionGraph           │
		├────────────────────────────────────┤
		│ Nodes: {                           │
		│   "GET_users": {                   │
		│     Status: Included               │
		│     Source: OpenApi                │
		│     DependsOn: []                  │
		│     Reason: "From new spec"        │
		│   },                               │
		│   "POST_users": {                  │
		│     Status: Modified               │
		│     Source: ExistingConfig         │
		│     Change: "URL changed"          │
		│     Reason: "Op now supports v2"   │
		│   },                               │
		│   "DELETE_users": {                │
		│     Status: Excluded               │
		│     Source: ExistingConfig         │
		│     Reason: "Removed from OpenAPI" │
		│   }                                │
		│ }                                  │
		│ Issues: [                          │
		│   {Level: Warning, Message: "..." }│
		│ ]                                  │
		│ Statistics: {                      │
		│   TotalOperations: 9               │
		│   IncludedOperations: 6            │
		│   ExcludedOperations: 3            │
		│   ModifiedOperations: 1            │
		│   ...                              │
		│ }                                  │
		└────────────────────────────────────┘
		 │         │           │          │
	┌────┴────┐    │        ┌──┴──────┐   └─┐
	▼         ▼    │        ▼         ▼     ▼
  JSON    Mermaid  │    PlantUML   Graphviz Report
  Dump   Diagram   │    Diagram    Format  (CSV)
		(Visual)   │
				   ▼
		┌────────────────────────────────┐
		│ ConversionResult (ENHANCED)    │
		├────────────────────────────────┤
		│ Success: bool                  │
		│ Config: ApimConfiguration      │
		│ HCL: string                    │
		│ Warnings: [...]                │
		│ Errors: [...]                  │
		│                                │
		│ ExecutionGraph: {...} ◄─ NEW! │
		│ TrackingReport: {...}  ◄─ NEW!│
		├────────────────────────────────┤
		│ OperationTrackingReport:       │
		│ {                              │
		│   OperationsByStatus: {        │
		│     Included: [...]            │
		│     Excluded: [...]            │
		│     Modified: [...]            │
		│     Blocked: [...]             │
		│   }                            │
		│   Deltas: [                    │
		│     {OperationId, ChangeType,  │
		│      FromValue, ToValue, ...}  │
		│   ]                            │
		│   Dependencies: [              │
		│     {Source, Target, Type}     │
		│   ]                            │
		│ }                              │
		└────────────────────────────────┘
					│
		┌───────────┼────────────┐
		▼           ▼            ▼
   ┌─────────┐ ┌────────┐ ┌──────────┐
   │ API     │ │ UI     │ │ Reports  │
   │ Output  │ │ Charts │ │ Export   │
   └─────────┘ └────────┘ └──────────┘


## Data Flow Example: Scenario

### Input
```
OpenAPI spec has:
├─ GET /users (New)
├─ GET /users/{id} (Existing)
├─ PUT /users/{id} (Existing, URL changed from /user/{id})
└─ POST /users (New)

Existing Terraform has:
├─ GET /users/{id}
├─ PUT /user/{id}
├─ DELETE /users (Custom, not in OpenAPI)
└─ PATCH /users (Custom, not in OpenAPI)
```

### Execution Graph Output
```
Operation: GET_users
├─ Status: Included
├─ Source: OpenApi
├─ Reason: From new OpenAPI spec
├─ DependsOn: []
└─ ReferencedBy: []

Operation: GET_users_id
├─ Status: Included (Unchanged)
├─ Source: OpenApi
├─ Reason: Exists in both configs
├─ DependsOn: []
└─ ReferencedBy: [PUT_users_id]

Operation: PUT_users_id
├─ Status: Modified
├─ Source: ExistingConfig (preserved)
├─ Reason: URL template has changed
├─ DependsOn: [GET_users_id]  ◄─ Dependency analysis
├─ ReferencedBy: []
├─ Change: UrlTemplateChanged
│   From: /user/{id}
│   To: /users/{id}
└─ Recommendation: Review if URL change is intentional

Operation: POST_users
├─ Status: Included
├─ Source: OpenApi
├─ Reason: New operation from spec
├─ DependsOn: []
└─ ReferencedBy: []

Operation: DELETE_users
├─ Status: Excluded
├─ Source: ExistingConfig
├─ Reason: Not in new OpenAPI spec
├─ Intent: Custom operation, preserving in merge
├─ DependsOn: []
└─ ReferencedBy: []

Operation: PATCH_users
├─ Status: Excluded
├─ Source: ExistingConfig
├─ Reason: Not in new OpenAPI spec
├─ Intent: Custom operation, preserving in merge
├─ DependsOn: []
└─ ReferencedBy: []
```

### Statistics
```
Total Operations: 6
├─ Included: 4
├─ Excluded (Preserved): 2
├─ Modified: 1
├─ New: 2
├─ Deleted: 0
├─ Blocked: 0
└─ Completion %: 100%
```

### Visualization (Mermaid)
```
graph TD
	GET_users["🆕 GET /users"]
	GET_users_id["✓ GET /users/{id}"]
	PUT_users_id["⚠️ PUT /users/{id}<br/>(URL changed)"]
	POST_users["🆕 POST /users"]
	DELETE_users["❌ DELETE /users<br/>(Preserved)"]
	PATCH_users["❌ PATCH /users<br/>(Preserved)"]

	GET_users_id -->|used by| PUT_users_id

	classDef included fill:#90EE90
	classDef modified fill:#FFD700
	classDef excluded fill:#FFB6C6
	classDef new fill:#87CEEB

	class GET_users new
	class GET_users_id included
	class PUT_users_id modified
	class POST_users new
	class DELETE_users excluded
	class PATCH_users excluded
```

### Tracking Report (CSV Export)
```
OperationId,Source,Status,ChangeType,OldValue,NewValue,DependsOn,Issues
GET_users,OpenApi,Included,Added,,GET /users,,
GET_users_id,OpenApi,Included,Unchanged,,GET /users/{id},,
PUT_users_id,ExistingConfig,Modified,UrlTemplateChanged,"/user/{id}","/users/{id}",GET_users_id,URL changed - review intentional
POST_users,OpenApi,Included,Added,,POST /users,,
DELETE_users,ExistingConfig,Excluded,Removed,,DELETE /users,,Custom operation - preserved
PATCH_users,ExistingConfig,Excluded,Removed,,PATCH /users,,Custom operation - preserved
```


## Integration Points

### 1. ConversionOrchestrator Integration
```csharp
var result = orchestrator.Convert(openApiJson, settings);

if (result.ExecutionGraph != null)
{
	// Check for blocked operations
	var blocked = result.ExecutionGraph.Nodes.Values
		.Where(n => n.Status == OperationStatus.Blocked)
		.ToList();

	if (blocked.Any())
	{
		logger.LogWarning("Blocked operations detected: {Count}", blocked.Count);
		foreach(var op in blocked)
			logger.LogWarning("  - {OperationId}: {Reason}", op.OperationId, op.Reason);
	}
}
```

### 2. MCP Tool Integration
```csharp
[McpServerTool(Name = "get_operation_graph")]
public static string GetOperationGraph(
	IConversionOrchestrator orchestrator,
	string openApiJson,
	ConversionSettings settings,
	string? exportFormat = "json")  // json|mermaid|plantuml
{
	var result = orchestrator.Convert(openApiJson, settings);

	if (result.ExecutionGraph == null)
		return "Error: No execution graph generated";

	return exportFormat switch
	{
		"mermaid" => graphBuilder.ExportToVisualization(
			result.ExecutionGraph,
			VisualizationFormat.Mermaid),
		"plantuml" => graphBuilder.ExportToVisualization(
			result.ExecutionGraph,
			VisualizationFormat.PlantUML),
		_ => JsonSerializer.Serialize(result.ExecutionGraph)
	};
}
```

### 3. API Endpoint Integration
```
GET /api/operations/graph?apiJson=...&format=mermaid
GET /api/operations/tracking-report?apiJson=...&mergeWith=existingHcl
GET /api/operations/deltas?apiJson=...
GET /api/operations/dependencies?apiJson=...
```
