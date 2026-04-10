using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TerraformApi.Api.Tests.Endpoints;

/// <summary>
/// Integration tests for the /api/environments endpoint that serves
/// pre-configured APIM environment presets from appsettings.json.
/// </summary>
public class EnvironmentsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public EnvironmentsEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetEnvironments_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/environments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetEnvironments_ReturnsDictionary()
    {
        var response = await _client.GetAsync("/api/environments");
        var content = await response.Content.ReadAsStringAsync();
        var envs = JsonSerializer.Deserialize<Dictionary<string, EnvironmentDto>>(content, JsonOptions);

        Assert.NotNull(envs);
    }

    [Fact]
    public async Task GetEnvironments_ContainsDevPreset()
    {
        var response = await _client.GetAsync("/api/environments");
        var content = await response.Content.ReadAsStringAsync();
        var envs = JsonSerializer.Deserialize<Dictionary<string, EnvironmentDto>>(content, JsonOptions);

        Assert.NotNull(envs);
        Assert.True(envs.ContainsKey("dev"));
        Assert.NotNull(envs["dev"].StageGroupName);
        Assert.NotNull(envs["dev"].ApimName);
    }

    private sealed class EnvironmentDto
    {
        public string? StageGroupName { get; set; }
        public string? ApimName { get; set; }
        public string? ApiGatewayHost { get; set; }
        public bool SubscriptionRequired { get; set; }
        public bool IncludeCorsPolicy { get; set; }
    }
}
