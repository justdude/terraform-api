using TerraformApi.Api.Dtos;
using TerraformApi.Api.Endpoints;
using TerraformApi.Application;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----

// Bind APIM environment presets from appsettings.json
builder.Services.Configure<Dictionary<string, ApimEnvironmentConfig>>(
    builder.Configuration.GetSection("ApimEnvironments"));

builder.Services.AddApplicationServices();

// CORS: restrict origins in production, open in development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("AllowedCorsOrigins")
            .Get<string[]>();

        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            // Development fallback — allow any origin
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

builder.Services.AddEndpointsApiExplorer();

// Limit request body to 10 MB to prevent abuse
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
});

var app = builder.Build();

// ---- Middleware pipeline ----

// Global exception handler — never leak stack traces to clients
app.UseExceptionHandler(error =>
{
    error.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "An unexpected error occurred. Check server logs for details."
        });
    });
});

// HTTPS redirect in non-development environments
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseStaticFiles();

app.MapConversionEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
