using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Location;

public sealed class AzureMapsLocationResolver : ILocationResolver
{
    public const string GeocodePath = "/geocode";
    public const string ReverseGeocodePath = "/reverseGeocode";
    public const string ApiVersion = "2023-06-01";
    public const string TokenScope = "https://atlas.microsoft.com/.default";
    public const string AtlasHost = "https://atlas.microsoft.com";

    private static readonly TokenRequestContext MapsTokenContext = new([TokenScope]);

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<AzureMapsLocationResolver> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public AzureMapsLocationResolver(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<AzureMapsLocationResolver> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(20);
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

        var cacheKey = McpCacheKeys.Maps(trimmed);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            var fromCache = JsonSerializer.Deserialize<ResolvedLocation>(cached.Value, CacheJson);
            if (fromCache is not null)
            {
                _logger.LogInformation(
                    "Location resolver cache hit. InvocationId={InvocationId} Key={Key} Lat={Lat} Lon={Lon} PostalCode={PostalCode}",
                    invocationId,
                    cacheKey,
                    fromCache.Latitude,
                    fromCache.Longitude,
                    fromCache.PostalCode);
                if (!string.IsNullOrWhiteSpace(fromCache.PostalCode) || !NeedsUsZip(fromCache))
                {
                    return fromCache;
                }

                _logger.LogInformation(
                    "Location resolver cache missing ZIP. InvocationId={InvocationId} Key={Key} Lat={Lat} Lon={Lon}",
                    invocationId,
                    cacheKey,
                    fromCache.Latitude,
                    fromCache.Longitude);
                try
                {
                    var cachedToken = await AcquireTokenAsync(invocationId, cancellationToken).ConfigureAwait(false);
                    var completed = await FillUsZipAsync(fromCache, cachedToken, invocationId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(completed.PostalCode))
                    {
                        await _cache.SetAsync(
                            cacheKey,
                            JsonSerializer.Serialize(completed),
                            _options.MapsCacheTtl,
                            invocationId,
                            cancellationToken).ConfigureAwait(false);
                    }

                    return completed;
                }
                catch (LocationResolutionException ex)
                {
                    _logger.LogWarning(
                        "Location resolver kept cached place without ZIP. InvocationId={InvocationId} Error={Error}",
                        invocationId,
                        ex.ErrorCode);
                    return fromCache;
                }
            }
        }

        var token = await AcquireTokenAsync(invocationId, cancellationToken).ConfigureAwait(false);

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

            location = await FillUsZipAsync(location, token, invocationId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Location resolver succeeded. InvocationId={InvocationId} Lat={Lat} Lon={Lon} Address={Address} PostalCode={PostalCode}",
                invocationId,
                location.Latitude,
                location.Longitude,
                location.FormattedAddress,
                location.PostalCode);

            await _cache.SetAsync(
                cacheKey,
                JsonSerializer.Serialize(location),
                _options.MapsCacheTtl,
                invocationId,
                cancellationToken).ConfigureAwait(false);

            return location;
        }
    }


    private async Task<string> AcquireTokenAsync(string invocationId, CancellationToken cancellationToken)
    {
        var tokenStarted = Stopwatch.StartNew();
        try
        {
            var token = await _credential.GetTokenAsync(MapsTokenContext, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Location resolver token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                tokenStarted.ElapsedMilliseconds);
            return token.Token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Location resolver token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                tokenStarted.ElapsedMilliseconds);
            throw new LocationResolutionException(
                "maps_token_failed",
                "Could not acquire an Azure Maps token. In Azure the Function managed identity needs Azure Maps Data Reader.");
        }
    }

    private async Task<ResolvedLocation> FillUsZipAsync(
        ResolvedLocation location,
        string token,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(location.PostalCode) || !NeedsUsZip(location))
        {
            return location;
        }

        var coordinates = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{location.Longitude},{location.Latitude}");
        var url = $"{AtlasHost}{ReverseGeocodePath}?api-version={ApiVersion}&coordinates={Uri.EscapeDataString(coordinates)}&resultTypes=Address,Postcode1";
        _logger.LogInformation(
            "Location resolver reverse geocode. InvocationId={InvocationId} Lat={Lat} Lon={Lon}",
            invocationId,
            location.Latitude,
            location.Longitude);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("x-ms-client-id", _options.AzureMapsClientId);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Location resolver reverse response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);
            if (!response.IsSuccessStatusCode)
            {
                return location;
            }

            var zip = ReadPostalFromBody(body);
            if (string.IsNullOrWhiteSpace(zip))
            {
                _logger.LogInformation(
                    "Location resolver reverse geocode has no ZIP. InvocationId={InvocationId} Lat={Lat} Lon={Lon}",
                    invocationId,
                    location.Latitude,
                    location.Longitude);
                return location;
            }

            _logger.LogInformation(
                "Location resolver reverse geocode filled ZIP. InvocationId={InvocationId} Lat={Lat} Lon={Lon} PostalCode={PostalCode}",
                invocationId,
                location.Latitude,
                location.Longitude,
                zip);
            return location with { PostalCode = zip };
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            _logger.LogWarning(
                ex,
                "Location resolver reverse geocode failed. InvocationId={InvocationId} Lat={Lat} Lon={Lon}",
                invocationId,
                location.Latitude,
                location.Longitude);
            return location;
        }
    }

    private static bool NeedsUsZip(ResolvedLocation location)
    {
        if (!string.IsNullOrWhiteSpace(location.PostalCode))
        {
            return false;
        }

        var country = location.CountryRegion?.Trim();
        if (!string.IsNullOrWhiteSpace(country))
        {
            return country.Equals("US", StringComparison.OrdinalIgnoreCase)
                || country.Equals("USA", StringComparison.OrdinalIgnoreCase)
                || country.Equals("United States", StringComparison.OrdinalIgnoreCase)
                || country.Equals("United States of America", StringComparison.OrdinalIgnoreCase);
        }

        return IsUsPoint(location.Latitude, location.Longitude);
    }

    private static bool IsUsPoint(double latitude, double longitude)
    {
        if (latitude is >= 24.5 and <= 49.5 && longitude is >= -125 and <= -66.5)
        {
            return true;
        }

        if (latitude is >= 51 and <= 72 && longitude is >= -170 and <= -129)
        {
            return true;
        }

        if (latitude is >= 18.5 and <= 22.5 && longitude is >= -161 and <= -154)
        {
            return true;
        }

        return latitude is >= 17.5 and <= 18.6 && longitude is >= -67.5 and <= -64.5;
    }

    private static string? ReadPostalFromBody(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (!document.RootElement.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var properties)
                    || !properties.TryGetProperty("address", out var address)
                    || address.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var zip = ReadUsZip(address);
                if (!string.IsNullOrWhiteSpace(zip))
                {
                    return zip;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
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
                string? postalCode = null;

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
                        postalCode = ReadUsZip(address);
                    }
                }

                location = new ResolvedLocation(lat, lon, query, formatted, locality, admin, country, postalCode);
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadUsZip(JsonElement address)
    {
        var raw = ReadString(address, "postalCode") ?? ReadString(address, "extendedPostalCode");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var countryIso = ReadCountryIso(address);
        if (countryIso is not null
            && !countryIso.Equals("US", StringComparison.OrdinalIgnoreCase)
            && !countryIso.Equals("USA", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length < 5 || !trimmed.Take(5).All(char.IsDigit))
        {
            return null;
        }

        if (trimmed.Length == 5 || (trimmed.Length > 5 && trimmed[5] == '-'))
        {
            return trimmed[..5];
        }

        return null;
    }

    private static string? ReadCountryIso(JsonElement address)
    {
        if (!address.TryGetProperty("countryRegion", out var country))
        {
            return ReadString(address, "countryCode");
        }

        if (country.ValueKind == JsonValueKind.String)
        {
            var value = country.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        if (country.ValueKind == JsonValueKind.Object)
        {
            return ReadString(country, "ISO") ?? ReadString(country, "iso");
        }

        return null;
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

    private static TokenCredential CreateCredential()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")))
        {
            return new ManagedIdentityCredential();
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true
        });
    }
}
