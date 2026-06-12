# OpenAPI to Azure APIM Terraform Converter

A .NET 10 controller-based Web API that converts OpenAPI JSON specifications into Azure API Management (APIM) Terraform configurations. Includes interactive Swagger UI at `/swagger`, a web frontend with terminal styling, validation against Microsoft APIM naming rules, and support for updating existing Terraform configs while preserving custom operations.

## Architecture

Clean Architecture with clearly separated concerns:

```
src/
  TerraformApi.Domain/        # Models, interfaces, validation rules — no external dependencies
  TerraformApi.Application/   # Business logic (parser, generator, merger, validator, transformer)
  TerraformApi.Api/           # MVC controllers + Swagger UI + static frontend (wwwroot)
  TerraformApi.Mcp/           # MCP server for AI assistant integration (stdio transport)
tests/
  TerraformApi.Application.Tests/   # Unit tests for Application services (173 tests)
  TerraformApi.Api.Tests/           # Integration tests for API endpoints (34 tests)
  TerraformApi.Mcp.Tests/           # Unit tests for MCP server tools (67 tests)
```

## Features

- **Convert** OpenAPI JSON to Azure APIM Terraform configuration
- **Update** existing Terraform configs — merges new spec while preserving custom operations
- **Validate** OpenAPI specs against Microsoft APIM naming rules
- **Environment presets** loaded from `appsettings.json` via `/api/environments`
- **CORS policy** generation with configurable origins and allowed methods
- **APIM naming validation** per Microsoft documentation:
  - API name: 1–256 chars, alphanumeric + hyphens, must start/end with alphanumeric
  - Operation ID: 1–80 chars, alphanumeric + hyphens + underscores
  - Display name: 1–300 chars, no `* # & + : < > ?`
  - API path: 0–400 chars, alphanumeric + hyphens + dots + slashes
  - Resource group: 1–90 chars, alphanumeric + hyphens + underscores + dots + parentheses
- **Fetch operations** from any OpenAPI/Swagger URL — returns methods, paths, parameters, tags
- **Cross-environment transform** — sync Terraform configs between dev/staging/prod, matching operations by URL+method
- **Append-only sync** — AST-based synchronization of existing Terraform with an OpenAPI spec: new operations are appended in the file's detected templating style, nothing is ever deleted (see below)
- **Analyze** — inspect an existing Terraform file: API groups, operation counts, detected `${...}` placeholder style, duplicates
- **Template profiles** — templatize literals (`apim-company-dev` → `${apim_name}`) or resolve placeholders for a specific environment
- **Swagger UI** at `/swagger` — interactive documentation and try-it-out for every endpoint (controller-based API with XML doc comments)
- Web UI with drag-and-drop file upload, no Node.js required
- **MCP server** with 10 tools for AI assistant integration (VS Code, Claude Code, Claude Desktop) — shares the exact same Application-layer services and input validation as the HTTP API
- Docker-ready for cloud deployment

## Append-only sync

`POST /api/sync` (or the `sync_openapi_with_terraform` MCP tool) merges an OpenAPI spec into an existing Terraform config with **append-only semantics**:

- Operations **only in OpenAPI** are appended to `api_operations`, rendered in the templating style detected from the existing file, each with a descriptive comment block (`# GET /users/{id} | op_id: ...`).
- Operations **only in Terraform** are always preserved untouched.
- Operations **in both** (matched by URL+method, then operation id) are enriched only where a field is missing — by default `operation_id`, `method` and `url_template` are never modified.
- Unmodified parts of the file are written back **byte-for-byte** (the HCL parser keeps source spans and only re-renders changed nodes).
- When new operations introduce `${...}` placeholders, a `REPLACE BEFORE APPLY` header is added before `api_operations` listing every variable to define.

Example request:

```json
POST /api/sync
{
  "openApiJson": "{ ...OpenAPI 3.x... }",
  "existingTerraform": "apis = { bpc_apis = { backend_apis = { ... } } }",
  "environment": "dev",
  "apiGroupName": "${api_group_name}",
  "stageGroupName": "rg-apim-dev",
  "apimName": "apim-company-dev",
  "apiPathPrefix": "myapp",
  "apiPathSuffix": "api",
  "apiGatewayHost": "api.dev.company.com",
  "backendServicePath": "my-service",
  "templateProfileName": "Auto",
  "operationFieldOverrides": { "description": "Overwrite" },
  "variableContext": { "env": "dev", "api_name": "bpc" }
}
```

Response: `{ "success": true, "terraformConfig": "...final HCL...", "report": { "operationsAdded": 1, "operationsPreserved": 5, "operationsEnriched": 0, "operationsIdentical": 8, "diffs": [...], "duplicates": [], "warnings": [...] } }`

Related endpoints: `POST /api/analyze-terraform` (read-only analysis) and `POST /api/apply-template-profile` (Templatize ⇄ Resolve). Default merge policies are documented in [docs/sync-policies.md](docs/sync-policies.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (optional, for containerised deployment)

---

## Running the Projects

### Running the API (serves the web UI)

```bash
# From the repository root — restores packages, compiles, and starts on http://localhost:5000
dotnet run --project src/TerraformApi.Api

# Or with a custom port
dotnet run --project src/TerraformApi.Api --urls "http://localhost:7000"
```

Open `http://localhost:5000` in your browser to use the web UI.

### Running in Development Mode

The `Development` environment loads `appsettings.Development.json`, which overrides the `dev` environment preset with a localhost gateway and enables debug logging:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/TerraformApi.Api
```

### Running Tests

```bash
# Run all tests (274 total: Application + API + MCP)
dotnet test

# Run only Application (unit) tests — 173 tests
dotnet test tests/TerraformApi.Application.Tests

# Run only API (integration) tests — 34 tests
dotnet test tests/TerraformApi.Api.Tests

# Run only MCP server (unit) tests — 67 tests
dotnet test tests/TerraformApi.Mcp.Tests

# With detailed output
dotnet test --verbosity normal

# With test results in TRX format (useful for CI)
dotnet test --logger "trx;LogFileName=results.trx"
```

### Building Without Running

```bash
# Restore packages and compile the entire solution
dotnet build

# Build in Release mode
dotnet build -c Release
```

---

## Docker

### Build and Run with Docker Compose

```bash
# Build the image and start the container
docker compose up --build

# Run in background
docker compose up --build -d

# Access at http://localhost:8080
```

### Build and Run the Docker Image Directly

```bash
# Build the image
docker build -t terraform-api .

# Run the container
docker run -p 8080:8080 terraform-api

# Access at http://localhost:8080
```

### Environment Variables in Docker

| Variable | Default | Description |
|---|---|---|
| `ASPNETCORE_URLS` | `http://+:8080` | Listening URL inside the container |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Determines which `appsettings.*.json` overrides load |

---

## Configuration: Environment Presets

`appsettings.json` contains an `ApimEnvironments` section with named environment presets. These are served to the frontend via `GET /api/environments` so the UI can auto-fill APIM settings.

```json
{
  "ApimEnvironments": {
    "dev": {
      "stageGroupName": "rg-apim-dev",
      "apimName": "apim-company-dev",
      "apiGatewayHost": "api.dev.company.com",
      "frontendHost": "portal",
      "companyDomain": "company.com",
      "localDevHost": "localhost",
      "localDevPort": "3000",
      "subscriptionRequired": false,
      "includeCorsPolicy": true,
      "allowedMethods": ["GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS"]
    },
    "staging": { ... },
    "prod": { ... }
  }
}
```

Add custom environments by adding new keys under `ApimEnvironments`. The `appsettings.Development.json` file can override specific presets for local development.

---

## Using the Web UI

1. Start the API (`dotnet run --project src/TerraformApi.Api`)
2. Open `http://localhost:5000` in your browser
3. Select an environment from the dropdown — the sidebar fields auto-fill from the preset
4. Choose a tab: **Convert**, **Update**, or **Validate**
5. Paste or drag-drop your OpenAPI JSON into the input pane
6. Click the action button — the Terraform output appears on the right
7. Copy or download the output

---

## API Endpoints

### GET /api/environments

Returns all configured APIM environment presets from `appsettings.json`.

```bash
curl http://localhost:5000/api/environments
```

```json
{
  "dev": {
    "stageGroupName": "rg-apim-dev",
    "apimName": "apim-company-dev",
    "apiGatewayHost": "api.dev.company.com",
    "subscriptionRequired": false,
    "includeCorsPolicy": true
  }
}
```

---

### POST /api/convert

Converts an OpenAPI JSON spec into a Terraform configuration.

```bash
curl -X POST http://localhost:5000/api/convert \
  -H "Content-Type: application/json" \
  -d '{
    "openApiJson": "{\"openapi\":\"3.0.1\",\"info\":{\"title\":\"My API\",\"version\":\"1.0.0\"},\"paths\":{\"/users\":{\"get\":{\"operationId\":\"listUsers\",\"summary\":\"List Users\",\"responses\":{\"200\":{\"description\":\"OK\"}}}}}}",
    "environment": "dev",
    "apiGroupName": "my-api-group",
    "stageGroupName": "rg-apim-dev",
    "apimName": "apim-company-dev",
    "apiPathPrefix": "myapp",
    "apiPathSuffix": "api",
    "apiGatewayHost": "api.dev.company.com",
    "backendServicePath": "my-service",
    "includeCorsPolicy": false
  }'
```

**Response:**

```json
{
  "success": true,
  "terraformConfig": "my-api-group = {\n  ...\n}",
  "warnings": [],
  "errors": [],
  "summary": {
    "apiName": "my-api-dev",
    "displayName": "My API - dev",
    "path": "myapp.dev/v1/api",
    "operationCount": 1,
    "operations": [
      { "operationId": "my-api-listusers-dev", "method": "GET", "urlTemplate": "users" }
    ]
  }
}
```

**Optional request fields:**

| Field | Type | Description |
|---|---|---|
| `apiName` | string | Override the auto-generated API name |
| `apiDisplayName` | string | Override the display name (defaults to OpenAPI `info.title`) |
| `apiVersion` | string | API version string, default `"v1"` |
| `revision` | string | APIM revision, default `"1"` |
| `productId` | string | APIM product ID to associate the API with |
| `operationPrefix` | string | Prefix for generated operation IDs |
| `subscriptionRequired` | bool | Whether a subscription key is required, default `false` |
| `includeCorsPolicy` | bool | Whether to generate a CORS policy block, default `false` |
| `frontendHost` | string | Frontend subdomain (e.g. `"portal"`) used in CORS origins |
| `companyDomain` | string | Company domain (e.g. `"company.com"`) for CORS origin construction |
| `localDevHost` | string | Local dev host for CORS (e.g. `"localhost"`) |
| `localDevPort` | string | Local dev port for CORS (e.g. `"3000"`) |
| `allowedMethods` | string[] | HTTP methods for CORS policy |

---

### POST /api/convert/update

Updates existing Terraform with changes from a new OpenAPI spec. Operations present in the existing Terraform but absent from the new spec are preserved (useful for custom/manually-added operations).

```bash
curl -X POST http://localhost:5000/api/convert/update \
  -H "Content-Type: application/json" \
  -d '{
    "openApiJson": "{ ... new spec ... }",
    "existingTerraform": "my-api-group = { ... existing HCL ... }",
    "environment": "dev",
    "apiGroupName": "my-api-group",
    "stageGroupName": "rg-apim-dev",
    "apimName": "apim-company-dev",
    "apiPathPrefix": "myapp",
    "apiPathSuffix": "api",
    "apiGatewayHost": "api.dev.company.com",
    "backendServicePath": "my-service",
    "includeCorsPolicy": false
  }'
```

Returns the same response shape as `/api/convert`. Returns `400 Bad Request` if `existingTerraform` is empty or missing.

---

### POST /api/validate

Validates an OpenAPI spec for APIM compatibility without generating Terraform. Returns detected operations and any naming rule violations.

```bash
curl -X POST http://localhost:5000/api/validate \
  -H "Content-Type: application/json" \
  -d '{
    "openApiJson": "{ ... }",
    "environment": "dev",
    "apiGroupName": "my-api-group",
    "stageGroupName": "rg-apim-dev",
    "apimName": "apim-company-dev",
    "apiPathPrefix": "myapp",
    "apiPathSuffix": "api",
    "apiGatewayHost": "api.dev.company.com",
    "backendServicePath": "my-service"
  }'
```

**Response:**

```json
{
  "isValid": true,
  "errors": [],
  "summary": {
    "apiName": "my-api-dev",
    "displayName": "My API - dev",
    "path": "myapp.dev/v1/api",
    "operationCount": 3,
    "operations": [...]
  }
}
```

Returns `400 Bad Request` if the JSON cannot be parsed at all.

---

### GET /api/health

Health check endpoint. Returns `200 OK` with `"Healthy"`.

```bash
curl http://localhost:5000/api/health
```

---

## Generated Terraform Format

Output matches the APIM Terraform module structure used by the included example:

```hcl
my-api-group = {
  product = []
  api = [
    {
        apim_resource_group_name         = "rg-apim-dev"
        apim_name                        = "apim-company-dev"
        name                             = "my-api-dev"
        display_name                     = "My API - dev"
        path                             = "myapp.dev/v1/api"
        service_url                      = "https://api.dev.company.com/v1/my-service/"
        protocols                        = ["https"]
        revision                         = "1"
        soap_pass_through                = false
        subscription_required            = false
        product_id                       = null
        subscription_key_parameter_names = null

        policy = <<XML
        <policies>
          <inbound>
            <cors allow-credentials="true">
              <allowed-origins>
                <origin>https://portal.dev.company.com</origin>
              </allowed-origins>
              ...
            </cors>
          </inbound>
        </policies>
        XML
    },
  ]

  api_operations = [
    {
        operation_id             = "my-api-listusers-dev"
        apim_resource_group_name = "rg-apim-dev"
        apim_name                = "apim-company-dev"
        api_name                 = "my-api-dev"
        display_name             = "List Users"
        method                   = "GET"
        url_template             = "users"
        status_code              = "200"
        description              = ""
    },
  ]
}
```

---

## Deployment via SSH

A deployment script is included for pushing to any Linux host with Docker installed.

### Prerequisites on the Remote Server

```bash
# Docker Engine 20.10+
curl -fsSL https://get.docker.com | sh

# Add your deploy user to the docker group (no sudo needed)
sudo usermod -aG docker deploy

# Create the install directory
sudo mkdir -p /opt/terraform-api && sudo chown deploy:deploy /opt/terraform-api
```

### Configure

```bash
# Copy and edit the environment file
cp .env.example .env
# Set at minimum: REMOTE_HOST, REMOTE_USER
```

### Deploy

```bash
# Full deploy: build image locally, push via SCP, start on remote
./deploy/deploy.sh

# Or with CLI flags (overrides .env)
./deploy/deploy.sh --host 10.0.0.5 --user deploy --key ~/.ssh/id_ed25519

# Build only (no push)
./deploy/deploy.sh --build-only
```

### Rollback / Stop

```bash
./deploy/rollback.sh --host 10.0.0.5
```

### What the Deploy Script Does

1. Builds the Docker image locally (`docker build`)
2. Saves and compresses the image to a `.tar.gz`
3. Copies the archive + `docker-compose.yml` + `.env` to the remote host via SCP
4. Loads the image on the remote host (`docker load`)
5. Starts the service (`docker compose up -d`)
6. Waits for the health check to pass
7. Cleans up the archive on both sides

### CORS in Production

Set allowed origins in `.env` or `appsettings.json`:

```bash
# In .env (overrides appsettings via env vars)
AllowedCorsOrigins__0=https://your-frontend.com
AllowedCorsOrigins__1=https://admin.your-frontend.com
```

Or in `appsettings.json`:
```json
{
  "AllowedCorsOrigins": ["https://your-frontend.com"]
}
```

When `AllowedCorsOrigins` is empty (default), development mode allows all origins. In production, set this explicitly.

---

## MCP Server (Model Context Protocol)

An MCP server is included so AI assistants (Claude Code, Claude Desktop, etc.) can call the conversion tools directly — no HTTP needed.

### Architecture

The MCP server (`src/TerraformApi.Mcp`) is a .NET console app that communicates via **stdio** transport. It references the Application and Domain layers directly, so there's no HTTP overhead.

```
src/TerraformApi.Mcp/
  Program.cs                      # Host setup with stdio transport + HttpClient DI
  Tools/
    ConvertTool.cs                # convert_openapi_to_terraform
    UpdateTool.cs                 # update_terraform_from_openapi
    ValidateTool.cs               # validate_openapi_for_apim
    EnvironmentsTool.cs           # list_environment_presets
    TransformEnvironmentTool.cs   # transform_environment
    FetchOperationsTool.cs        # fetch_openapi_operations
    ParseTerraformOperationsTool.cs # parse_terraform_operations
    SyncTool.cs                   # sync_openapi_with_terraform
    AnalyzeTool.cs                # analyze_terraform_apim
    ApplyTemplateProfileTool.cs   # apply_template_profile
  appsettings.json                # Environment presets (same as API project)
```

### Available Tools

| Tool | Description |
|---|---|
| `fetch_openapi_operations` | Fetches an OpenAPI spec from a URL and returns a structured list of all operations (methods, paths, parameters, tags) |
| `parse_terraform_operations` | Parses a Terraform APIM config and returns the operations list in the same format as `fetch_openapi_operations` |
| `convert_openapi_to_terraform` | Converts an OpenAPI JSON spec into a full APIM Terraform HCL block; optional `templateProfileName` produces templated `${...}` output with operation comments |
| `update_terraform_from_openapi` | Merges a new OpenAPI spec into existing Terraform, preserving custom operations |
| `sync_openapi_with_terraform` | **Append-only sync**: appends missing operations in the file's detected templating style, preserves everything else, returns a full diff report. Configurable per-field merge policy and match strategy |
| `analyze_terraform_apim` | Read-only analysis of an existing Terraform file: API groups, operation counts, detected templating profile, referenced `${...}` variables, duplicates |
| `apply_template_profile` | One-time `Templatize` (literals → `${...}` placeholders) or `Resolve` (placeholders → literals for a given environment) |
| `validate_openapi_for_apim` | Validates an OpenAPI spec against APIM naming rules (no Terraform output) |
| `list_environment_presets` | Lists configured APIM environment presets (resource groups, hosts, etc.) |
| `transform_environment` | Transforms Terraform from one APIM environment to another, with optional merge/sync against existing target |

### Tool Parameters

#### `convert_openapi_to_terraform`

**Required parameters:**

| Parameter | Description | Example |
|---|---|---|
| `openApiJson` | OpenAPI 3.x JSON string | `{"openapi":"3.0.1",...}` |
| `environment` | Target environment | `"dev"`, `"staging"`, `"prod"` |
| `apiGroupName` | Terraform variable group name | `"my-api-group"` |
| `stageGroupName` | Azure resource group name | `"rg-apim-dev"` |
| `apimName` | APIM instance name | `"apim-company-dev"` |
| `apiPathPrefix` | API path prefix | `"myapp"` |
| `apiPathSuffix` | API path suffix | `"api"` |
| `apiGatewayHost` | Gateway hostname | `"api.dev.company.com"` |
| `backendServicePath` | Backend service path | `"my-service"` |

**Optional parameters:** `apiName`, `apiDisplayName`, `apiVersion` (default `"v1"`), `revision` (default `"1"`), `subscriptionRequired` (default `false`), `includeCorsPolicy` (default `false`), `frontendHost`, `companyDomain`, `localDevHost`, `localDevPort`, `productId`, `generateProduct` (default `false`), `productDisplayName`, `productDescription`, `productSubscriptionRequired`, `productApprovalRequired`

#### `update_terraform_from_openapi`

Same required parameters as `convert_openapi_to_terraform`, plus:

| Parameter | Description |
|---|---|
| `existingTerraform` | The existing Terraform HCL to merge into |

#### `validate_openapi_for_apim`

| Parameter | Required | Description |
|---|---|---|
| `openApiJson` | Yes | OpenAPI 3.x JSON string to validate |
| `environment` | No | Environment name for operation ID generation (default: `"dev"`) |

#### `fetch_openapi_operations`

| Parameter | Required | Description | Example |
|---|---|---|---|
| `openApiUrl` | Yes | URL to an OpenAPI/Swagger JSON endpoint | `"https://api.example.com/swagger/v1/swagger.json"` |

Returns a JSON object with `api` info (title, version, description, sourceUrl), `totalOperations` count, and an `operations` array. Each operation contains: `method`, `urlTemplate`, `path`, `operationId`, `description`, `tags[]`, `parameters[]` (name, in, type, required), `requestBodyContentTypes[]`, and `responseCodes[]`.

#### `transform_environment`

**Required parameters:**

| Parameter | Description | Example |
|---|---|---|
| `sourceTerraform` | Source environment's Terraform HCL | The dev config text |
| `targetEnvironment` | Target environment name | `"staging"`, `"prod"` |
| `targetStageGroupName` | Target resource group | `"rg-apim-staging"` |
| `targetApimName` | Target APIM instance | `"apim-company-staging"` |
| `targetApiGatewayHost` | Target gateway hostname | `"api-staging.company.com"` |

**Optional parameters:** `sourceEnvironment` (auto-detected if omitted), `existingTargetTerraform` (for merge/sync), `targetFrontendHost`, `targetCompanyDomain`, `targetLocalDevHost`, `targetLocalDevPort`, `targetSubscriptionRequired`

When `existingTargetTerraform` is provided, operations are matched by **url_template + HTTP method** (not by operation_id, since IDs differ between environments). Source operations are synced, target-only operations are preserved.

#### `list_environment_presets`

| Parameter | Required | Description |
|---|---|---|
| `environmentName` | No | Specific environment to retrieve. Omit to list all. |

#### `sync_openapi_with_terraform`

**Required parameters:** `existingTerraform` (empty string → generate from scratch), `environment`, `apiGroupName`, `stageGroupName`, `apimName`, `apiPathPrefix`, `apiPathSuffix`, `apiGatewayHost`, `backendServicePath`, and one of `openApiJson` / `openApiUrl`.

**Optional parameters:**

| Parameter | Default | Description |
|---|---|---|
| `templateProfileName` | `"Auto"` | Style for new operations: `Auto` (detect from file), `UserExampleProfile`, `ExtendedProfile`, `LiteralProfile` |
| `operationFieldOverridesJson` | — | Per-field policy, e.g. `{"description":"Overwrite"}` (`Preserve` / `EnrichIfMissing` / `Overwrite`) |
| `matchKeysJson` | `["MethodAndUrl","OperationId","Tag"]` | Match key order |
| `variableContextJson` | — | Variables for resolved-mode matching, e.g. `{"env":"dev"}` |
| `addOperationComments` | `true` | Comment block above each inserted operation |
| `addReplaceBeforeApplyHeader` | `true` | Placeholder header before `api_operations` |

Returns JSON: `{ success, terraformConfig, report { operationsAdded, operationsPreserved, operationsEnriched, operationsIdentical, diffs, duplicates, warnings }, executionGraph { nodes, statistics } }`.

#### `analyze_terraform_apim`

| Parameter | Required | Description |
|---|---|---|
| `existingTerraform` | Yes | The Terraform HCL to analyze (read-only) |

Returns JSON: `{ success, apiGroups [{ apimResourceGroupName, apiName, operationCount }], totalOperations, detectedProfile { confidence, closestKnownProfileName, detectedFields, allReferencedVariables }, duplicates }`.

#### `apply_template_profile`

| Parameter | Required | Description |
|---|---|---|
| `existingTerraform` | Yes | The Terraform HCL to convert |
| `direction` | Yes | `Templatize` (literals → `${...}`) or `Resolve` (`${...}` → literals) |
| `profileName` | Templatize only | `UserExampleProfile` / `ExtendedProfile` / `LiteralProfile` |
| `variableContextJson` | Resolve only | Variable values, e.g. `{"env":"dev","apim_name":"apim-company-dev"}` |
| `overwriteExisting` | No (`false`) | Replace literals that already have values (Templatize) |

Returns JSON: `{ success, terraformConfig, appliedChanges, warnings }`.

### Running Standalone

```bash
# From the repository root
dotnet run --project src/TerraformApi.Mcp
```

The server communicates over stdio (stdin/stdout) using [JSON-RPC 2.0](https://www.jsonrpc.org/specification) per the [MCP specification](https://modelcontextprotocol.io/). It is not meant to be used interactively — connect it to an MCP-compatible client.

### Adding to VS Code (Copilot / Claude Extension)

The repository includes a `.vscode/mcp.json` that registers the MCP server automatically when you open the workspace:

```json
{
  "servers": {
    "terraform-api": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "${workspaceFolder}/src/TerraformApi.Mcp/TerraformApi.Mcp.csproj",
        "--no-launch-profile"
      ]
    }
  }
}
```

**Steps:**
1. Open this repository in VS Code
2. Install a MCP-compatible extension (e.g. [Claude for VS Code](https://marketplace.visualstudio.com/items?itemName=anthropic.claude-code), GitHub Copilot with MCP support)
3. The server is auto-discovered from `.vscode/mcp.json` — no manual config needed
4. All 6 tools appear in the extension's tool list

> **Tip:** For faster startup, use a published binary instead of `dotnet run` (see "Published Binary" below).

### Adding to Claude Code

Add to your project's `.mcp.json` or global Claude Code settings:

```json
{
  "mcpServers": {
    "terraform-api": {
      "command": "dotnet",
      "args": ["run", "--project", "src/TerraformApi.Mcp"]
    }
  }
}
```

### Adding to Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "terraform-api": {
      "command": "dotnet",
      "args": ["run", "--project", "/full/path/to/src/TerraformApi.Mcp"]
    }
  }
}
```

### Published Binary (Alternative)

For faster startup, publish the MCP server as a self-contained binary:

```bash
dotnet publish src/TerraformApi.Mcp -c Release -o ./publish/mcp
```

Then reference the binary directly in your MCP config:

```json
{
  "mcpServers": {
    "terraform-api": {
      "command": "/full/path/to/publish/mcp/TerraformApi.Mcp"
    }
  }
}
```

### Testing the MCP Server

#### 1. Unit Tests (recommended first step)

```bash
# Run all 67 MCP tool tests
dotnet test tests/TerraformApi.Mcp.Tests
```

Tests call tool methods directly with real Application services — no mocking of business logic, only HTTP is mocked where needed.

#### 2. MCP Inspector (interactive browser UI)

The [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector) provides a web-based UI to explore and call MCP tools:

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/TerraformApi.Mcp/TerraformApi.Mcp.csproj
```

This opens a browser window where you can:
- See all 6 tools listed with their parameters
- Call any tool with custom inputs and see the response
- Verify JSON-RPC communication works end-to-end

#### 3. Manual stdio testing

Pipe JSON-RPC messages directly to the server process:

```bash
# Initialize the server and list tools
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' | dotnet run --project src/TerraformApi.Mcp
```

Expected: two JSON-RPC responses on stdout — an `initialize` result and a `tools/list` result containing all 6 tools.

#### 4. VS Code integration test

1. Open the repository in VS Code
2. Verify the MCP server starts (check the extension's MCP status)
3. Ask the AI to `"List the APIM environment presets"` — it should invoke `list_environment_presets`
4. Ask `"Fetch the operations from https://petstore3.swagger.io/api/v3/openapi.json"` — it should invoke `fetch_openapi_operations`

### Customising Environment Presets

Edit `src/TerraformApi.Mcp/appsettings.json` to configure environment presets. The `list_environment_presets` tool reads from this file to provide auto-fill values.

### MCP Server Tests

The MCP tools are tested independently from the MCP transport layer. Tests call the tool methods directly with real Application services (no mocking of business logic):

```bash
dotnet test tests/TerraformApi.Mcp.Tests
```

67 tests cover:
- **FetchOperationsTool** (18 tests): spec parsing, URL validation, HTTP mocking, parameters, tags, request bodies, response codes, error handling
- **ConvertTool** (11 tests): valid conversion, error handling, CORS, products, custom names, multi-environment
- **UpdateTool** (6 tests): merge operations, add operations, error handling, CORS in updates
- **ValidateTool** (11 tests): valid specs, invalid JSON, operation detection, environment suffixes, fallback IDs
- **TransformEnvironmentTool** (8 tests): dev→staging, merge/sync, subscription override, error cases, preserved operations
- **EnvironmentsTool** (13 tests): list all, specific lookup, missing configs, field formatting, error handling

---

## Cloud Deployment

The API is stateless and container-ready:

- **Azure Container Apps**: Deploy the Docker image, set `ASPNETCORE_ENVIRONMENT` and `ASPNETCORE_URLS`
- **Azure App Service**: Use the Docker deployment option with the included `Dockerfile`
- **Kubernetes**: Standard `Deployment` + `Service` manifest pointing to the Docker image
- **Any Linux host**: Use the included `deploy/deploy.sh` script for SSH-based deployment

The `/api/health` endpoint can serve as the liveness/readiness probe.

### Security Hardening Checklist

- [ ] Set `AllowedCorsOrigins` to your frontend domain(s)
- [ ] Use HTTPS (terminate TLS at load balancer or reverse proxy)
- [ ] Restrict network access to port 8080 (firewall/security groups)
- [ ] Review `appsettings.json` environment presets — remove `company.com` placeholders
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Consider adding a reverse proxy (nginx/Caddy) for TLS termination and rate limiting

---

## License

MIT
