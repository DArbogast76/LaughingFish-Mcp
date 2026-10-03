using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Proxies stored sea-condition observations. Not a forecast.
/// </summary>
public sealed class GetSeaConditionsTool
{
    public const string ToolName = "get_sea_conditions";
    public const string ToolDescription =
        "Observed sea conditions at the nearest reporting stations: significant wave height, dominant and average period, wave direction, sustained wind, gust, barometric pressure, and station air temperature. Recent history, not a forecast and not a continuous sea-state curve. Pass place (preferred) or latitude and longitude. A place is resolved to coordinates before the query. Optional nearest 1, 3, or 5 (default 3). Optional days 1, 3, 7, 45, or 90 (default 1). Days is trailing UTC hours of lookback, not a date range. days 1 is the current observed sea. days 3 or 7 shows whether the sea has been building or easing. days 45 or 90 is a longer observed pattern. None of these is a forecast. Optional maxDistanceMiles 10, 25, or 50 (default 50). sensors says which of wave height, wave direction, wind, and pressure that station reports. no_station_within_range and no_readings_in_window are successful empty results. Do not invent a wave height, period, or wind.";

    public const string PublicSummary =
        "Observed sea conditions from stored meteorological reports. This is the recent history at the reporting station, not a forecast and not a continuous sea-state curve.";

    public const string PublicStation =
        "latitude and longitude are the station coordinates. distanceMiles is the distance from the requested coordinate. sensors says which measurement families the station has reported. Station names and station ids are not returned.";

    public const string WindowPurpose =
        " A 1-day window is the current sea. A 3- or 7-day window shows whether height, period, wind, and pressure have been building or easing. A 45- or 90-day window is the longer observed pattern. latest is the current snapshot even when days is greater than 1. None of these is a forecast.";

    private static readonly int[] AllowedNearest = [1, 3, 5];
    private static readonly int[] AllowedDays = [1, 3, 7, 45, 90];
    private static readonly int[] AllowedMaxDistance = [10, 25, 50];

    private readonly ILogger<GetSeaConditionsTool> _logger;
    private readonly ISeaConditionsApiClient _client;
    private readonly ILocationResolver _locations;

    public GetSeaConditionsTool(
        ILogger<GetSeaConditionsTool> logger,
        ISeaConditionsApiClient client,
        ILocationResolver locations)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
    }

    [Function(nameof(GetSeaConditionsTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Preferred over raw coordinates. Resolved to coordinates before the query.", false)] string? place,
        [McpToolProperty("latitude", "Latitude in decimal degrees when place is not provided.", false)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees when place is not provided.", false)] double? longitude,
        [McpToolProperty("nearest", "How many in-range stations to return. Allowed: 1, 3, 5. Default 3.", false)] int? nearest,
        [McpToolProperty("days", "Trailing UTC hours of lookback. Allowed: 1, 3, 7, 45, 90. Default 1. Not a date range and not a forecast.", false)] int? days,
        [McpToolProperty("maxDistanceMiles", "Maximum station distance in miles. Allowed: 10, 25, 50. Default 50.", false)] int? maxDistanceMiles,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetSeaConditions tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Place={Place} Lat={Lat} Lon={Lon} Nearest={Nearest} Days={Days} MaxDistanceMiles={MaxDistanceMiles}",
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
            var result = await ExecuteAsync(
                place,
                latitude,
                longitude,
                nearest,
                days,
                maxDistanceMiles,
                invocationId,
                functionContext.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetSeaConditions tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSeaConditions tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal async Task<JsonObject> ExecuteAsync(
        string? place,
        double? latitude,
        double? longitude,
        int? nearest,
        int? days,
        int? maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveInt(nearest, AllowedNearest, 3, out var resolvedNearest))
        {
            return Error("invalid_nearest", "nearest must be 1, 3, or 5.", invocationId);
        }

        if (!TryResolveInt(days, AllowedDays, 1, out var resolvedDays))
        {
            return Error("invalid_days", "days must be 1, 3, 7, 45, or 90.", invocationId);
        }

        if (!TryResolveInt(maxDistanceMiles, AllowedMaxDistance, 50, out var resolvedMiles))
        {
            return Error("invalid_max_distance", "maxDistanceMiles must be 10, 25, or 50.", invocationId);
        }

        double lat;
        double lon;
        JsonNode? locationPayload;

        if (!string.IsNullOrWhiteSpace(place))
        {
            try
            {
                var resolved = await _locations.ResolveAsync(place, invocationId, cancellationToken).ConfigureAwait(false);
                lat = resolved.Latitude;
                lon = resolved.Longitude;
                locationPayload = JsonSerializer.SerializeToNode(ResolveLocationTool.ToPayload(resolved));
            }
            catch (LocationResolutionException ex)
            {
                _logger.LogInformation(
                    "GetSeaConditions location failed. InvocationId={InvocationId} Error={Error}",
                    invocationId,
                    ex.ErrorCode);
                return Error(ex.ErrorCode, ex.Message, invocationId);
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
            locationPayload = new JsonObject
            {
                ["latitude"] = lat,
                ["longitude"] = lon
            };
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
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "GetSeaConditions downstream unsuccessful. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                result.ErrorCode,
                result.StatusCode);
            var failed = Error(result.ErrorCode ?? "sea_conditions_http_error", null, invocationId);
            if (result.StatusCode > 0)
            {
                failed["statusCode"] = result.StatusCode;
            }

            return failed;
        }

        var payload = ParseNode(result.Body);
        ShapeForModel(payload);
        var status = payload is JsonObject body ? body["status"]?.GetValue<string>() : null;
        var stationCount = payload is JsonObject parsed && parsed["stations"] is JsonArray stations
            ? stations.Count
            : 0;
        _logger.LogInformation(
            "GetSeaConditions downstream succeeded. InvocationId={InvocationId} Status={Status} StationCount={StationCount} Nearest={Nearest} Days={Days} MaxDistanceMiles={MaxDistanceMiles}",
            invocationId,
            status,
            stationCount,
            resolvedNearest,
            resolvedDays,
            resolvedMiles);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["location"] = locationPayload,
            ["result"] = payload
        };
    }

    internal static void ShapeForModel(JsonNode? node)
    {
        StripStationIds(node);
        if (node is not JsonObject root || root["explanation"] is not JsonObject explanation)
        {
            return;
        }

        explanation["summary"] = PublicSummary;
        explanation["station"] = PublicStation;
        var window = explanation["window"]?.GetValue<string>() ?? string.Empty;
        if (!window.Contains("1-day window", StringComparison.Ordinal))
        {
            explanation["window"] = window.TrimEnd() + WindowPurpose;
        }
    }

    private static bool TryResolveInt(int? value, int[] allowed, int fallback, out int resolved)
    {
        if (value is null or 0)
        {
            resolved = fallback;
            return true;
        }

        if (allowed.Contains(value.Value))
        {
            resolved = value.Value;
            return true;
        }

        resolved = fallback;
        return false;
    }

    private static void StripStationIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("stationId");
            foreach (var property in obj.ToList())
            {
                StripStationIds(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                StripStationIds(item);
            }
        }
    }

    private static JsonObject Error(string? error, string? message, string invocationId)
    {
        var body = new JsonObject
        {
            ["ok"] = false,
            ["error"] = error,
            ["invocationId"] = invocationId
        };
        if (!string.IsNullOrWhiteSpace(message))
        {
            body["message"] = message;
        }

        return body;
    }

    private static JsonNode ParseNode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(body) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return JsonValue.Create(body)!;
        }
    }
}
