using System.Diagnostics;
using System.Text.Json;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Proxies observed water temperature from the WaterTemp API.
/// Days is lookback history for trend, not a forecast.
/// </summary>
public sealed class GetWaterTemperatureTool
{
    public const string ToolName = "get_water_temperature";
    public const string ToolDescription =
        "Observed water temperature: current reading and hourly history for trend (warming, cooling, or steady). Days is lookback, not a forecast. Pass place (preferred) or latitude and longitude. Optional nearest 1, 3, or 5 (default 1). Optional days 1, 3, 7, 30, or 90 (default 1). Optional maxDistanceMiles 10, 25, or 50 (default 25). Returns no station when none is within range. Does not forecast water temperature and does not return air weather or sunrise. Do not invent a temperature when Maps or the API fails.";

    private static readonly int[] AllowedNearest = [1, 3, 5];
    private static readonly int[] AllowedDays = [1, 3, 7, 30, 90];
    private static readonly int[] AllowedMaxDistance = [10, 25, 50];

    private readonly ILogger<GetWaterTemperatureTool> _logger;
    private readonly IWaterTempApiClient _client;
    private readonly ILocationResolver _locations;
    private readonly McpOptions _options;

    public GetWaterTemperatureTool(
        ILogger<GetWaterTemperatureTool> logger,
        IWaterTempApiClient client,
        ILocationResolver locations,
        IOptions<McpOptions> options)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
        _options = options.Value;
    }

    [Function(nameof(GetWaterTemperatureTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Example: Galveston, Texas. Preferred over raw coordinates.", false)] string? place,
        [McpToolProperty("latitude", "Latitude in decimal degrees when place is not provided.", false)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees when place is not provided.", false)] double? longitude,
        [McpToolProperty("nearest", "How many in-range stations to return. Allowed: 1, 3, 5. Default 1.", false)] int? nearest,
        [McpToolProperty("days", "History lookback in days for trend. Allowed: 1, 3, 7, 30, 90. Default 1. Not a forecast.", false)] int? days,
        [McpToolProperty("maxDistanceMiles", "Maximum station distance in miles. Allowed: 10, 25, 50. Default 25.", false)] int? maxDistanceMiles,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "GetWaterTemperature tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Place={Place} Lat={Lat} Lon={Lon} Nearest={Nearest} Days={Days} MaxDistanceMiles={MaxDistanceMiles}",
            invocationId,
            context.Name,
            context.SessionId,
            place,
            latitude,
            longitude,
            nearest,
            days,
            maxDistanceMiles);

        try
        {
            if (!TryResolveInt(nearest, AllowedNearest, 1, out var resolvedNearest))
            {
                return Error("invalid_nearest", "nearest must be 1, 3, or 5.", invocationId);
            }

            if (!TryResolveInt(days, AllowedDays, 1, out var resolvedDays))
            {
                return Error("invalid_days", "days must be 1, 3, 7, 30, or 90.", invocationId);
            }

            if (!TryResolveInt(maxDistanceMiles, AllowedMaxDistance, 25, out var resolvedMiles))
            {
                return Error("invalid_max_distance", "maxDistanceMiles must be 10, 25, or 50.", invocationId);
            }

            double lat;
            double lon;
            object? locationPayload = null;

            if (!string.IsNullOrWhiteSpace(place))
            {
                try
                {
                    var resolved = await _locations.ResolveAsync(place, invocationId, functionContext.CancellationToken)
                        .ConfigureAwait(false);
                    lat = resolved.Latitude;
                    lon = resolved.Longitude;
                    locationPayload = ResolveLocationTool.ToPayload(resolved);
                }
                catch (LocationResolutionException ex)
                {
                    _logger.LogInformation(
                        "GetWaterTemperature tool location failed. InvocationId={InvocationId} Error={Error} ElapsedMs={ElapsedMs}",
                        invocationId,
                        ex.ErrorCode,
                        started.ElapsedMilliseconds);
                    return new
                    {
                        ok = false,
                        error = ex.ErrorCode,
                        message = ex.Message,
                        invocationId
                    };
                }
            }
            else if (latitude is { } parsedLat && longitude is { } parsedLon)
            {
                if (parsedLat is < -90 or > 90 || parsedLon is < -180 or > 180
                    || double.IsNaN(parsedLat) || double.IsNaN(parsedLon)
                    || double.IsInfinity(parsedLat) || double.IsInfinity(parsedLon))
                {
                    return Error("invalid_coordinates", "latitude must be -90 to 90 and longitude must be -180 to 180.", invocationId);
                }

                lat = parsedLat;
                lon = parsedLon;
            }
            else
            {
                return Error("missing_location", "Provide place, or latitude and longitude.", invocationId);
            }

            var result = await _client.GetAsync(
                lat,
                lon,
                resolvedNearest,
                resolvedDays,
                resolvedMiles,
                invocationId,
                functionContext.CancellationToken).ConfigureAwait(false);

            object? payload = null;
            if (!string.IsNullOrWhiteSpace(result.Body))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<JsonElement>(result.Body);
                }
                catch (JsonException)
                {
                    payload = result.Body;
                }
            }

            _logger.LogInformation(
                "GetWaterTemperature tool finished. InvocationId={InvocationId} Success={Success} StatusCode={StatusCode} Error={Error} ElapsedMs={ElapsedMs}",
                invocationId,
                result.IsSuccess,
                result.StatusCode,
                result.ErrorCode,
                started.ElapsedMilliseconds);

            if (!result.IsSuccess)
            {
                return new
                {
                    ok = false,
                    error = result.ErrorCode,
                    message = result.ErrorMessage,
                    statusCode = result.StatusCode == 0 ? (int?)null : result.StatusCode,
                    invocationId,
                    waterTempApiBound = _options.WaterTempApiBound,
                    waterTempApiAudienceBound = _options.WaterTempApiAudienceBound,
                    body = payload
                };
            }

            return new
            {
                ok = true,
                invocationId,
                source = "LaughingFish.WaterTempApi",
                statusCode = result.StatusCode,
                location = locationPayload ?? new { latitude = lat, longitude = lon },
                result = payload
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetWaterTemperature tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    private static bool TryResolveInt(int? value, int[] allowed, int fallback, out int resolved)
    {
        if (value is null)
        {
            resolved = fallback;
            return true;
        }

        resolved = value.Value;
        return allowed.Contains(resolved);
    }

    private static object Error(string code, string message, string invocationId) => new
    {
        ok = false,
        error = code,
        message,
        invocationId
    };
}
