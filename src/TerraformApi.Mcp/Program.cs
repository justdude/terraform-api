using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using TerraformApi.Application;
using TerraformApi.Domain.Models;

var builder = Host.CreateApplicationBuilder(args);

// Ensure appsettings.json is loaded from the binary's directory (not just CWD),
// since MCP servers are often launched via `dotnet run --project` from a different directory.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);

// Redirect all logging to stderr — stdout is reserved for JSON-RPC messages
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Bind APIM environment presets from appsettings.json (same section as the API project)
builder.Services.Configure<Dictionary<string, ApimEnvironmentConfig>>(
    builder.Configuration.GetSection("ApimEnvironments"));

builder.Services.AddHttpClient();
builder.Services.AddApplicationServices();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
