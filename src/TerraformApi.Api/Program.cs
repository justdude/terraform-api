using TerraformApi.Api.Endpoints;
using TerraformApi.Application;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

app.MapConversionEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
