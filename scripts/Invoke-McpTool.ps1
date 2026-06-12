<#
.SYNOPSIS
Calls a single tool on the TerraformApi MCP server over stdio JSON-RPC — no AI
client required. Designed for CI / post-build automation.

.EXAMPLE
.\Invoke-McpTool.ps1 -Tool list_environment_presets

.EXAMPLE
.\Invoke-McpTool.ps1 -Tool convert_openapi_to_terraform `
    -ArgumentsJson '{"openApiUrl":"https://localhost:7166/swagger/v1/swagger.json","environment":"dev"}'

.NOTES
Prints the tool's text result to stdout. Exits 1 when the server cannot be
started or the tool call fails at the protocol level.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tool,

    [string]$ArgumentsJson = '{}',

    # Path to TerraformApi.Mcp.dll. Defaults to the repo's Debug (then Release) build.
    [string]$ServerDll = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ServerDll)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $candidates = @(
        (Join-Path $repoRoot 'src\TerraformApi.Mcp\bin\Debug\net10.0\TerraformApi.Mcp.dll'),
        (Join-Path $repoRoot 'src\TerraformApi.Mcp\bin\Release\net10.0\TerraformApi.Mcp.dll')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { $ServerDll = $candidate; break }
    }
    if ([string]::IsNullOrWhiteSpace($ServerDll)) {
        throw "TerraformApi.Mcp.dll not found. Build the solution first (dotnet build)."
    }
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'dotnet'
$psi.Arguments = '"' + $ServerDll + '"'
$psi.WorkingDirectory = Split-Path -Parent $ServerDll
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false

$proc = [System.Diagnostics.Process]::Start($psi)

function Read-Response([int]$ExpectedId, [int]$TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $task = $proc.StandardOutput.ReadLineAsync()
        if (-not $task.Wait(($deadline - (Get-Date)))) { break }
        $line = $task.Result
        if ($null -eq $line) { break }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $json = $line | ConvertFrom-Json } catch { continue }
        if ($json.PSObject.Properties['id'] -and $json.id -eq $ExpectedId) { return $json }
    }
    throw "No response for request $ExpectedId. stderr: $($proc.StandardError.ReadToEnd())"
}

try {
    # Handshake
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"Invoke-McpTool","version":"1.0"}}}')
    $proc.StandardInput.Flush()
    Read-Response 1 | Out-Null
    $proc.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')

    # Tool call — embed the caller's arguments JSON verbatim.
    $request = '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"' + $Tool + '","arguments":' + $ArgumentsJson + '}}'
    $proc.StandardInput.WriteLine($request)
    $proc.StandardInput.Flush()

    $response = Read-Response 2

    if ($response.PSObject.Properties['error']) {
        throw "Tool call failed: $($response.error | ConvertTo-Json -Compress)"
    }

    # Every tool returns a single text content block.
    $text = $response.result.content[0].text

    if ($response.result.PSObject.Properties['isError'] -and $response.result.isError) {
        throw "Tool '$Tool' returned an error: $text"
    }

    $text
}
finally {
    if (-not $proc.HasExited) { $proc.Kill() }
    $proc.Dispose()
}
