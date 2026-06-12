# Using the converter — with MCP and without

The conversion engine is exposed three ways, all backed by the **same
Application-layer services** with identical validation and output:

| Surface | Best for | Transport |
|---|---|---|
| MCP server (`TerraformApi.Mcp`) | AI assistants and **headless automation** | stdio JSON-RPC |
| HTTP API (`TerraformApi.Api`) | Web UI, Swagger try-it-out, service-to-service | HTTP + JSON |
| PowerShell scripts (`scripts/`) | CI pipelines, post-build hooks | wraps the MCP server |

---

## 1. With an MCP client (AI assistant)

The repository ships `.vscode/mcp.json`, so opening the workspace in VS Code
registers the server automatically. For Claude Desktop / Claude Code, add:

```json
{
  "mcpServers": {
    "terraform-api": {
      "command": "dotnet",
      "args": ["run", "--project", "D:/Projects/terraform-api/src/TerraformApi.Mcp"]
    }
  }
}
```

Then talk to the assistant naturally:

- *"Convert this swagger.json to APIM Terraform for the dev environment"* → `convert_openapi_to_terraform`
- *"What's in this Terraform file?"* → `analyze_terraform_apim`
- *"Add the new endpoints from this spec without touching anything existing"* → `sync_openapi_with_terraform`
- *"Generate an APIM product block"* → `generate_apim_product`

**You can omit any APIM setting** — missing values are generated as `{tag}`
placeholders with an explanatory header, so the assistant never blocks asking
for resource-group names you don't know yet.

## 2. Without MCP — HTTP API

Run the API (`dotnet run --project src/TerraformApi.Api`) and use:

- **Swagger UI** at `https://localhost:7166/swagger` — interactive docs for all 12 endpoints.
- Plain HTTP from any tool:

```powershell
Invoke-RestMethod -Method Post -Uri https://localhost:7166/api/convert `
  -ContentType 'application/json' `
  -Body (@{ openApiJson = [IO.File]::ReadAllText('swagger.json'); environment = 'dev' } | ConvertTo-Json)
```

The endpoint↔tool parity table lives in the README.

## 3. Without MCP client — headless automation (CI / post-build)

The MCP server is a plain console process speaking JSON-RPC on stdio, which
makes it a zero-dependency automation endpoint: no port, no running web
server, no AI client. Two scripts wrap it:

### `scripts/Invoke-McpTool.ps1` — call any tool

```powershell
# List environment presets:
.\scripts\Invoke-McpTool.ps1 -Tool list_environment_presets

# Analyze an existing Terraform file:
.\scripts\Invoke-McpTool.ps1 -Tool analyze_terraform_apim `
  -ArgumentsJson (@{ existingTerraform = [IO.File]::ReadAllText('apim.tf') } | ConvertTo-Json -Compress)
```

### `scripts/convert-after-build.ps1` — OpenAPI → Terraform in one call

```powershell
# Zero configuration — missing settings become {tag} placeholders:
.\scripts\convert-after-build.ps1 -OpenApi .\artifacts\swagger.json -OutputPath .\terraform\apim.tf

# Full configuration:
.\scripts\convert-after-build.ps1 -OpenApi https://localhost:7166/swagger/v1/swagger.json `
  -OutputPath .\terraform\apim.tf `
  -Environment dev -ApiGroupName my-api-group -StageGroupName rg-apim-dev `
  -ApimName apim-company-dev -ApiPathPrefix myapp -ApiPathSuffix api `
  -ApiGatewayHost api.dev.company.com -BackendServicePath my-service
```

The script exits non-zero on failure, so it is safe to gate a pipeline on it.

### Automating conversion after a project build

Add a post-build target to the **service project whose API you convert**
(works with any project that emits a swagger/OpenAPI file at build time, e.g.
via `Microsoft.Extensions.ApiDescription.Server` build-time generation):

```xml
<!-- In your service's .csproj -->
<Target Name="GenerateApimTerraform" AfterTargets="Build" Condition="'$(Configuration)' == 'Release'">
  <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildThisFileDirectory)..\..\terraform-api\scripts\convert-after-build.ps1&quot; -OpenApi &quot;$(OutputPath)swagger.json&quot; -OutputPath &quot;$(MSBuildThisFileDirectory)terraform\apim.tf&quot; -Environment dev" />
</Target>
```

Or as a CI step (GitHub Actions / Azure DevOps):

```yaml
- name: Generate APIM Terraform
  shell: pwsh
  run: |
    dotnet build terraform-api/terraform-api.slnx -c Release
    ./terraform-api/scripts/convert-after-build.ps1 `
      -OpenApi ./artifacts/swagger.json `
      -OutputPath ./terraform/apim.tf `
      -Environment $env:DEPLOY_ENV
```

Prerequisite in both cases: the solution must be built first so
`src/TerraformApi.Mcp/bin/<Config>/net10.0/TerraformApi.Mcp.dll` exists
(pass `-ServerDll` to point at a custom location).

### PowerShell 5.1 gotcha (already handled in the scripts)

When composing your own argument JSON, never feed `Get-Content -Raw` output
straight into `ConvertTo-Json` on Windows PowerShell 5.1 — the string carries
note properties and serializes as an **object**, which the tool rejects.
Use `[System.IO.File]::ReadAllText(...)` instead.

---

## Integration test coverage

`tests/TerraformApi.Mcp.Tests/Integration/` launches the **real server
process** over stdio and exercises these use cases end-to-end (the same path
the scripts and AI clients use):

| # | Use case |
|---|---|
| UC1 | Tool discovery — `tools/list` returns all 11 tools |
| UC2 | Post-build conversion with zero settings → placeholder-tagged output |
| UC3 | Full conversion with complete settings → no placeholders |
| UC4 | Analyze an existing file, then append-only sync a new spec into it |
| UC5 | Pre-flight APIM naming validation |
| UC6 | Parameterless product block generation |
| UC7 | Environment presets served from the binary's appsettings.json |
| UC8 | Invalid input → structured error, server stays responsive |

Run them with:

```powershell
dotnet test tests/TerraformApi.Mcp.Tests --filter "FullyQualifiedName~Integration"
```
