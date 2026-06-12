<#
.SYNOPSIS
Converts an OpenAPI specification to APIM Terraform after a project build —
headless, via the TerraformApi MCP server (no HTTP API, no AI client needed).

Any APIM setting you omit is generated as a replaceable {tag} placeholder and
documented in a header comment, so this works with zero configuration.

.EXAMPLE
# Convert a swagger.json produced by your build:
.\convert-after-build.ps1 -OpenApi .\bin\Release\swagger.json -OutputPath .\terraform\apim.tf

.EXAMPLE
# Convert from a running service, with real settings:
.\convert-after-build.ps1 -OpenApi https://localhost:7166/swagger/v1/swagger.json `
    -OutputPath .\terraform\apim.tf -Environment dev -ApiGroupName my-api-group `
    -StageGroupName rg-apim-dev -ApimName apim-company-dev

.EXAMPLE
# Wire into MSBuild (csproj) to run on every Release build:
#   <Target Name="GenerateApimTerraform" AfterTargets="Build" Condition="'$(Configuration)'=='Release'">
#     <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(SolutionDir)scripts\convert-after-build.ps1&quot; -OpenApi &quot;$(ProjectDir)openapi.json&quot; -OutputPath &quot;$(ProjectDir)terraform\apim.tf&quot;" />
#   </Target>
#>
[CmdletBinding()]
param(
    # Path to an OpenAPI JSON file, or an http(s) URL to fetch it from.
    [Parameter(Mandatory = $true)]
    [string]$OpenApi,

    # Where to write the generated Terraform.
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    # APIM settings — all optional; omitted ones become {tag} placeholders.
    [string]$Environment = '',
    [string]$ApiGroupName = '',
    [string]$StageGroupName = '',
    [string]$ApimName = '',
    [string]$ApiPathPrefix = '',
    [string]$ApiPathSuffix = '',
    [string]$ApiGatewayHost = '',
    [string]$BackendServicePath = '',

    [string]$ServerDll = ''
)

$ErrorActionPreference = 'Stop'

$arguments = @{}

if ($OpenApi -match '^https?://') {
    $arguments['openApiUrl'] = $OpenApi
}
else {
    if (-not (Test-Path $OpenApi)) {
        throw "OpenAPI file not found: $OpenApi"
    }
    # ReadAllText, NOT Get-Content -Raw: Windows PowerShell 5.1 decorates
    # Get-Content output with note properties (PSPath, ...) which makes
    # ConvertTo-Json serialize the string as an OBJECT and breaks the tool call.
    $arguments['openApiJson'] = [System.IO.File]::ReadAllText($OpenApi)
}

# Only pass settings the caller actually provided — the server fills the rest
# with placeholder tags.
if ($Environment)        { $arguments['environment'] = $Environment }
if ($ApiGroupName)       { $arguments['apiGroupName'] = $ApiGroupName }
if ($StageGroupName)     { $arguments['stageGroupName'] = $StageGroupName }
if ($ApimName)           { $arguments['apimName'] = $ApimName }
if ($ApiPathPrefix)      { $arguments['apiPathPrefix'] = $ApiPathPrefix }
if ($ApiPathSuffix)      { $arguments['apiPathSuffix'] = $ApiPathSuffix }
if ($ApiGatewayHost)     { $arguments['apiGatewayHost'] = $ApiGatewayHost }
if ($BackendServicePath) { $arguments['backendServicePath'] = $BackendServicePath }

$argumentsJson = $arguments | ConvertTo-Json -Compress -Depth 5

# Errors inside the invoked script surface as terminating exceptions
# ($ErrorActionPreference = 'Stop'); $LASTEXITCODE is NOT set by .ps1 calls.
$invokeScript = Join-Path $PSScriptRoot 'Invoke-McpTool.ps1'
$result = & $invokeScript -Tool 'convert_openapi_to_terraform' -ArgumentsJson $argumentsJson -ServerDll $ServerDll

$text = ($result | Out-String).TrimEnd()

if ($text.StartsWith('Conversion failed') -or $text.StartsWith('Conversion error')) {
    throw $text
}

$outputDir = Split-Path -Parent $OutputPath
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force $outputDir | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, $text + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host "Terraform written to $OutputPath"
if ($text.Contains('GENERATED WITH PLACEHOLDER TAGS')) {
    Write-Host 'NOTE: placeholder tags were used for missing settings - see the header comment in the output.'
}
