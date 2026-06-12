using System.Text.Json;

namespace TerraformApi.Application.Services;

/// <summary>
/// Resolves an OpenAPI specification from either direct JSON input or a URL.
/// Shared by the API controllers and the MCP tools so both hosts validate
/// input identically (absolute HTTP(S) URL, non-empty response, valid JSON).
/// </summary>
public static class OpenApiDocumentResolver
{
    /// <summary>
    /// Returns <paramref name="openApiJson"/> when provided; otherwise fetches
    /// from <paramref name="openApiUrl"/> and validates the response is JSON.
    /// Throws <see cref="InvalidOperationException"/> with a user-facing message
    /// on any failure.
    /// </summary>
    public static async Task<string> ResolveAsync(
        HttpClient httpClient,
        string? openApiJson,
        string? openApiUrl,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(openApiJson))
        {
            return openApiJson;
        }

        if (!string.IsNullOrWhiteSpace(openApiUrl))
        {
            if (!Uri.TryCreate(openApiUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new InvalidOperationException(
                    $"Invalid URL: '{openApiUrl}'. Must be an absolute HTTP(S) URL.");
            }

            try
            {
                var content = await httpClient.GetStringAsync(uri, cancellationToken);

                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException($"Empty response received from {openApiUrl}");

                // Validate it's actually JSON before handing it to the parser.
                using var document = JsonDocument.Parse(content);
                _ = document.RootElement;

                return content;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to fetch OpenAPI specification from {openApiUrl}: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"Request to '{openApiUrl}' timed out.", ex);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Response from {openApiUrl} is not valid JSON: {ex.Message}", ex);
            }
        }

        throw new InvalidOperationException("Either 'openApiJson' or 'openApiUrl' must be provided");
    }
}
