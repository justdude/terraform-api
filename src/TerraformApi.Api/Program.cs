using Microsoft.OpenApi.Models;
using TerraformApi.Api.Dtos;
using TerraformApi.Application;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----

// Bind APIM environment presets from appsettings.json
builder.Services.Configure<Dictionary<string, ApimEnvironmentConfig>>(
    builder.Configuration.GetSection("ApimEnvironments"));

builder.Services.AddHttpClient();
builder.Services.AddApplicationServices();

// Controller-based API. Automatic model-state validation is suppressed so the
// actions keep returning the project's own error response shapes instead of
// the default ProblemDetails.
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

// Swagger / OpenAPI document + UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Terraform API",
        Version = "v1",
        Description = "Converts OpenAPI specifications into Azure APIM Terraform configurations. " +
                      "Includes append-only sync, Terraform analysis, template profiles, " +
                      "cross-environment transform and APIM naming validation."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

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
app.UseDefaultFiles(); // "/" → wwwroot/index.html (the constrained SPA fallback below doesn't match the empty path)
app.UseStaticFiles();

// Swagger UI at /swagger
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    // Relative URL — resolves under /swagger and stays correct behind proxies/path bases.
    options.SwaggerEndpoint("v1/swagger.json", "Terraform API v1");
});

app.MapControllers();

// SPA fallback for the web frontend. Excludes /api and /swagger so those
// namespaces can never be answered with index.html (a cached HTML response
// for swagger.json renders as "definition does not specify a valid version
// field" in Swagger UI). The fallback HTML itself is marked no-store so
// browsers never cache it against an arbitrary URL.
app.MapFallbackToFile(
    "{*path:regex(^(?!api($|/)|swagger($|/)).*$)}",
    "index.html",
    new StaticFileOptions
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-store"
    });

app.Run();

public partial class Program { }
