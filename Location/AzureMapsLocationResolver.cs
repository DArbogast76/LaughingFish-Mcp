using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Location;

public sealed class AzureMapsLocationResolver : ILocationResolver
{
    public const string GeocodePath = "/geocode";
    public const string ApiVersion = "2023-06-01";
    public const string TokenScope = "https://atlas.microsoft.com/.default";
    public const string AtlasHost = "https://atlas.microsoft.com";

    private static readonly TokenRequestContext MapsTokenContext = new([TokenScope]);

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly ILogger<AzureMapsLocationResolver> _logger;
    private readonly DefaultAzureCredential _credential = new();

    public AzureMapsLocationResolver(
        HttpClient http,
        IOptions<McpOptions> options,
        ILogger<AzureMapsLocationResolver> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.3"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ResolvedLocation> ResolveAsync(
        string query,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new LocationResolutionException("invalid_place", "place is required.");
        }

        if (!_options.AzureMapsBound)
        {
            _logger.LogWarning(
                "Location resolver skipped. InvocationId={InvocationId} Reason=maps_unbound",
                invocationId);
            throw new LocationResolutionException(
                "maps_unbound",
                "AzureMapsClientId and AzureMapsEndpoint are not configured.");
        }

        AccessToken token;
        try
        {
            token = await _credential.GetTokenAsync(MapsTokenContext, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Location resolver token failure. InvocationId={InvocationId}",
                invocationId);
            throw new LocationResolutionException(
                "maps_token_failed",
                "Could not acquire an Azure Maps token. Locally run az login; in Azure the Function MI needs Azure Maps Data Reader.");
        }

        var url = $"{AtlasHost}{GeocodePath}?api-version={ApiVersion}&query={Uri.EscapeDataString(trimmed)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.TryAddWithoutValidation("x-ms-client-id", _options.AzureMapsClientId);

        _logger.LogInformation(
            "Location resolver request. InvocationId={InvocationId} Query={Query}",
            invocationId,
            trimmed);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Location resolver timeout. InvocationId={InvocationId}", invocationId);
            throw new LocationResolutionException("maps_timeout", "Azure Maps geocode timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Location resolver transport failure. InvocationId={InvocationId}", invocationId);
            throw new LocationResolutionException("maps_unreachable", "Azure Maps could not be reached.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Location resolver response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode)
            {
                throw new LocationResolutionException(
                    "maps_http_error",
                    $"Azure Maps geocode returned {(int)response.StatusCode}.");
            }

            if (!TryReadFeature(body, trimmed, out var location) || location is null)
            {
                throw new LocationResolutionException(
                    "place_not_found",
                    $"No coordinates were found for '{trimmed}'.");
            }

            _logger.LogInformation(
                "Location resolver succeeded. InvocationId={InvocationId} Lat={Lat} Lon={Lon} Address={Address}",
                invocationId,
                location.Latitude,
                location.Longitude,
                location.FormattedAddress);

            return location;
        }
    }

    private static bool TryReadFeature(string body, string query, out ResolvedLocation? location)
    {
        location = null;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("geometry", out var geometry)
                    || !geometry.TryGetProperty("coordinates", out var coordinates)
                    || coordinates.GetArrayLength() < 2)
                {
                    continue;
                }

                var lon = coordinates[0].GetDouble();
                var lat = coordinates[1].GetDouble();
                string? formatted = null;
                string? locality = null;
                string? admin = null;
                string? country = null;

                if (feature.TryGetProperty("properties", out var properties))
                {
                    formatted = ReadString(properties, "address", "formattedAddress")
                        ?? ReadString(properties, "formattedAddress");
                    if (properties.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
                    {
                        formatted ??= ReadString(address, "formattedAddress") ?? ReadString(address, "freeformAddress");
                        locality = ReadString(address, "locality") ?? ReadString(address, "municipality");
                        admin = ReadString(address, "adminDistrict") ?? ReadString(address, "countrySubdivision");
                        country = ReadString(address, "countryRegion") ?? ReadString(address, "countryCode");
                    }
                }

                location = new ResolvedLocation(lat, lon, query, formatted, locality, admin, country);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = node.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(node, child);
    }
}
