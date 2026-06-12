using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TerraformApi.Mcp.Tests.Integration;

/// <summary>
/// Launches the real MCP server process (dotnet TerraformApi.Mcp.dll) and talks
/// to it over stdio JSON-RPC — the exact transport an MCP client (or a CI
/// automation script) uses. One server instance is shared by all tests in the
/// class; calls are sequential with increasing request ids.
/// </summary>
public sealed class McpServerFixture : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StringBuilder _stderr = new();
    private int _nextId;

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(30);

    public McpServerFixture()
    {
        var serverDll = LocateServerDll();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{serverDll}\"",
            WorkingDirectory = Path.GetDirectoryName(serverDll)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the MCP server process.");
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        // Drain stderr in the background so the pipe never blocks; keep the
        // text for diagnostics on timeout.
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_stderr) _stderr.AppendLine(e.Data);
        };
        _process.BeginErrorReadLine();

        // MCP handshake.
        var init = Rpc("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "integration-tests", version = "1.0" }
        });
        if (init.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"initialize failed: {error}");

        Notify("notifications/initialized");
    }

    /// <summary>Sends a JSON-RPC request and returns the parsed response root.</summary>
    public JsonElement Rpc(string method, object? @params = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        });
        _stdin.WriteLine(request);
        _stdin.Flush();
        return ReadResponse(id);
    }

    /// <summary>Sends a JSON-RPC notification (no response expected).</summary>
    public void Notify(string method)
    {
        _stdin.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", method }));
        _stdin.Flush();
    }

    /// <summary>
    /// Calls an MCP tool and returns the text payload of the first content item
    /// (every tool in this server returns a single text block).
    /// </summary>
    public string CallTool(string name, object? arguments = null)
    {
        var response = Rpc("tools/call", new { name, arguments = arguments ?? new { } });

        if (response.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"tools/call {name} failed: {error}");

        return response.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()!;
    }

    /// <summary>Lists registered tool names.</summary>
    public List<string> ListToolNames()
    {
        var response = Rpc("tools/list", new { });
        return response.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!)
            .OrderBy(n => n)
            .ToList();
    }

    private JsonElement ReadResponse(int expectedId)
    {
        var deadline = DateTime.UtcNow + ResponseTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var readTask = _stdout.ReadLineAsync();
            if (!readTask.Wait(deadline - DateTime.UtcNow))
                break;

            var line = readTask.Result;
            if (line is null)
                throw new InvalidOperationException(
                    $"MCP server closed stdout unexpectedly. stderr:\n{StderrText()}");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue; // not JSON-RPC (defensive) — skip
            }

            using (doc)
            {
                // Skip server-initiated notifications; match our request id.
                if (doc.RootElement.TryGetProperty("id", out var idProp)
                    && idProp.ValueKind == JsonValueKind.Number
                    && idProp.GetInt32() == expectedId)
                {
                    return doc.RootElement.Clone();
                }
            }
        }

        throw new TimeoutException(
            $"No response for request {expectedId} within {ResponseTimeout.TotalSeconds}s. stderr:\n{StderrText()}");
    }

    private string StderrText()
    {
        lock (_stderr) return _stderr.ToString();
    }

    /// <summary>
    /// Finds the built MCP server dll for the same build configuration as the
    /// test assembly: &lt;repo&gt;/src/TerraformApi.Mcp/bin/&lt;Config&gt;/net10.0/.
    /// </summary>
    private static string LocateServerDll()
    {
        // Test output: <repo>/tests/TerraformApi.Mcp.Tests/bin/<Config>/net10.0/
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var configuration = new DirectoryInfo(baseDir).Parent!.Name; // e.g. "Debug"

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "terraform-api.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate the repository root from " + baseDir);

        var dll = Path.Combine(dir.FullName, "src", "TerraformApi.Mcp", "bin", configuration, "net10.0", "TerraformApi.Mcp.dll");
        if (!File.Exists(dll))
            throw new InvalidOperationException(
                $"MCP server binary not found at {dll}. Build the solution before running integration tests.");

        return dll;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
        _process.Dispose();
    }
}
