# OpenAPI to Azure APIM Terraform Converter

A .NET Minimal API that converts OpenAPI JSON specifications into Azure API Management (APIM) Terraform configurations. Includes a web frontend, validation against Microsoft APIM naming rules, and support for updating existing Terraform configs.

## Architecture

Clean Architecture with separated concerns:

```
src/
  TerraformApi.Domain/        # Models, interfaces, validation rules
  TerraformApi.Application/   # Business logic services (parser, generator, merger, validator)
  TerraformApi.Api/           # Minimal API endpoints + static frontend
tests/
  TerraformApi.Application.Tests/   # Unit tests for services
  TerraformApi.Api.Tests/           # Integration tests for endpoints
```

## Features

- **Convert** OpenAPI JSON to Azure APIM Terraform configuration
- **Update** existing Terraform configs with changes from OpenAPI specs (preserves custom operations)
- **Validate** OpenAPI specs against Microsoft APIM naming rules
- **CORS policy** generation with configurable origins and methods
- **APIM naming validation** per Microsoft documentation:
  - API name: 1-256 chars, alphanumeric + hyphens, must start/end with alphanumeric
  - Operation ID: 1-80 chars, alphanumeric + hyphens + underscores
  - Display name: 1-300 chars, no `* # & + : < > ?`
  - API path: 0-400 chars, alphanumeric + hyphens + dots + slashes
  - Resource group: 1-90 chars, alphanumeric + hyphens + underscores + dots + parentheses
- Web UI with drag-and-drop file upload, no Node.js required
- Docker-ready for cloud deployment

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- Docker (optional, for containerized deployment)

## Quick Start

### Local Development

```bash
# Restore and build
dotnet build

# Run the API (serves UI at http://localhost:5000)
dotnet run --project src/TerraformApi.Api

# Run tests
dotnet test
```

### Docker

```bash
# Build and run
docker compose up --build

# Access at http://localhost:8080
```

## API Endpoints

### POST /api/convert

Converts OpenAPI JSON to Terraform configuration.

**Request body:**
```json
{
  "openApiJson": "{ \"openapi\": \"3.0.1\", ... }",
  "environment": "dev",
  "apiGroupName": "my-api-group",
  "stageGroupName": "rg-apim-dev",
  "apimName": "apim-company-dev",
  "apiPathPrefix": "myapp",
  "apiPathSuffix": "api",
  "apiGatewayHost": "api.company.com",
  "backendServicePath": "my-service",
  "apiVersion": "v1",
  "revision": "1",
  "productId": "my-product",
  "frontendHost": "portal",
  "companyDomain": "company.com",
  "localDevHost": "localhost",
  "localDevPort": "3000",
  "includeCorsPolicy": true,
  "subscriptionRequired": false
}
```

**Response:**
```json
{
  "success": true,
  "terraformConfig": "my-api-group = { ... }",
  "warnings": [],
  "errors": [],
  "summary": {
    "apiName": "my-api-dev",
    "displayName": "My API - dev",
    "path": "myapp.dev/v1/api",
    "operationCount": 5,
    "operations": [...]
  }
}
```

### POST /api/convert/update

Updates existing Terraform with new OpenAPI spec. Same request as `/api/convert` plus an `existingTerraform` field. Operations not present in the new spec are preserved.

### POST /api/validate

Validates OpenAPI JSON for APIM compatibility. Returns detected operations and any naming violations.

### GET /api/health

Health check endpoint.

## Generated Terraform Format

Output matches the APIM module structure:

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
      service_url                      = "https://api.company.com/v1/my-service/"
      protocols                        = ["https"]
      revision                         = "1"
      soap_pass_through                = false
      subscription_required            = false
      product_id                       = "my-product"
      subscription_key_parameter_names = null

      policy = <<XML
      <policies>...</policies>
      XML
    },
  ]

  api_operations = [
    {
      operation_id             = "my-api-get-users-dev"
      method                   = "GET"
      url_template             = "users"
      ...
    },
  ]
}
```

## Cloud Deployment

The application is designed to run as a stateless cloud agent:

- **Docker**: Use the included `Dockerfile` and `docker-compose.yml`
- **Azure Container Apps / App Service**: Deploy the Docker image directly
- **Kubernetes**: Use the Docker image with a standard deployment manifest

The API accepts JSON requests and returns generated Terraform, making it suitable for CI/CD pipeline integration.

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `ASPNETCORE_URLS` | `http://+:8080` | Listening URL |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Environment name |

## License

MIT
