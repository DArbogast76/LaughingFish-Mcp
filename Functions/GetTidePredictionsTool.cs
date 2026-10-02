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
/// Proxies stored high and low tide predictions. Not observed water level.
/// </summary>
public sealed class GetTidePredictionsTool
{
    public const string ToolName = "get_tide_predictions";
    public const string ToolDescription =
        "Predicted high and low tide turns for a place and date range. Pass place or latitude and longitude. Pass start and end as yyyy-MM-dd, or omit them for today. Inclusive, at most 31 days. Convert relative dates before calling. Do not pass a time zone. Optional nearest 1, 3, or 5 (default 1). Optional maxDistanceMiles 10, 25, or 50 (default 25). Heights are predicted above Mean Lower Low Water, in feet and meters. type H is a high tide. type L is a low tide. This is not an observed water level, not tide direction, and not a continuous curve. no_station_within_range and no_predictions_in_window are successful empty results. Do not invent a tide.";

    private static readonly int[] AllowedNearest = [1, 3, 5];
    private static readonly int[] AllowedMaxDistance = [10, 25, 50];

    private readonly ILogger<GetTidePredictionsTool> _logger;
    private readonly ITideApiClient _client;
    private readonly ILocationResolver _locations;

    public GetTidePredictionsTool(
        ILogger<GetTidePredictionsTool> logger,
        ITideApiClient client,
        ILocationResolver locations)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
    }

    [Function(nameof(GetTidePredictionsTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Preferred over raw coordinates.", false)] string? place,
        [McpToolProperty("latitude", "Latitude in decimal degrees when place is not provided.", false)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees when place is not provided.", false)] double? longitude,
        [McpToolProperty("start", "Window start. yyyy-MM-dd. Defaults to today when omitted.", false)] string? start,
        [McpToolProperty("end", "Window end. yyyy-MM-dd, inclusive. Defaults to start. At most 31 days.", false)] string? end,
        [McpToolProperty("startDate", "Same as start. yyyy-MM-dd.", false)] string? startDate,
        [McpToolProperty("endDate", "Same as end. yyyy-MM-dd, inclusive.", false)] string? endDate,
        [McpToolProperty("nearest", "How many in-range stations to return. Allowed: 1, 3, 5. Default 1.", false)] int? nearest,
        [McpToolProperty("maxDistanceMiles", "Maximum station distance in miles. Allowed: 10, 25, 50. Default 25.", false)] int? maxDistanceMiles,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetTidePredictions tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Place={Place} Lat={Lat} Lon={Lon} Start={Start} End={End} StartDate={StartDate} EndDate={EndDate} Nearest={Nearest} MaxDistanceMiles={MaxDistanceMiles}",
            invocationId,
            context.Name,
            context.SessionId,
            place,
            latitude,
            longitude,
            start,
            end,
            startDate,
            endDate,
            nearest,
            maxDistanceMiles);

        try
        {
            var result = await ExecuteAsync(
                place,
                latitude,
                longitude,
                start,
                end,
                startDate,
                endDate,
                nearest,
                maxDistanceMiles,
                invocationId,
                functionContext.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetTidePredictions tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetTidePredictions tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal async Task<JsonObject> ExecuteAsync(
        string? place,
        double? latitude,
        double? longitude,
        string? start,
        string? end,
        string? startDate,
        string? endDate,
        int? nearest,
        int? maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveInt(nearest, AllowedNearest, 1, out var resolvedNearest))
        {
            return Error("invalid_nearest", "nearest must be 1, 3, or 5.", invocationId);
        }

        if (!TryResolveInt(maxDistanceMiles, AllowedMaxDistance, 25, out var resolvedMiles))
        {
            return Error("invalid_max_distance", "maxDistanceMiles must be 10, 25, or 50.", invocationId);
        }

        var windowStart = FirstNonEmpty(start, startDate);
        var windowEnd = FirstNonEmpty(end, endDate);
        if (!TryResolveWindow(windowStart, windowEnd, out var resolvedStart, out var resolvedEnd, out var windowError))
        {
            _logger.LogInformation(
                "GetTidePredictions window rejected. InvocationId={InvocationId} Start={Start} End={End} StartDate={StartDate} EndDate={EndDate} Error={Error}",
                invocationId,
                start,
                end,
                startDate,
                endDate,
                windowError);
            return Error("invalid_window", windowError, invocationId);
        }

        double lat;
        double lon;
        JsonNode? locationPayload = null;

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
                    "GetTidePredictions location failed. InvocationId={InvocationId} Error={Error}",
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
            resolvedStart,
            resolvedEnd,
            resolvedNearest,
            resolvedMiles,
            invocationId,
            cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "GetTidePredictions downstream unsuccessful. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                result.ErrorCode,
                result.StatusCode);
            var failed = Error(result.ErrorCode ?? "tide_predictions_http_error", null, invocationId);
            if (result.StatusCode > 0)
            {
                failed["statusCode"] = result.StatusCode;
            }

            return failed;
        }

        var payload = ParseNode(result.Body);
        StripStationIds(payload);
        var status = payload is JsonObject body ? body["status"]?.GetValue<string>() : null;
        _logger.LogInformation(
            "GetTidePredictions downstream succeeded. InvocationId={InvocationId} Status={Status} Start={Start} End={End} Nearest={Nearest} MaxDistanceMiles={MaxDistanceMiles}",
            invocationId,
            status,
            resolvedStart,
            resolvedEnd,
            resolvedNearest,
            resolvedMiles);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["location"] = locationPayload,
            ["result"] = payload
        };
    }

    internal static bool TryResolveWindow(
        string? start,
        string? end,
        out string resolvedStart,
        out string resolvedEnd,
        out string error)
    {
        resolvedStart = string.Empty;
        resolvedEnd = string.Empty;
        error = WindowError(start, end);

        var startDates = ReadDates(start);
        var endDates = ReadDates(end);
        if (startDates.Count == 0 && endDates.Count == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            resolvedStart = today;
            resolvedEnd = today;
            error = string.Empty;
            return true;
        }

        if (startDates.Count >= 2 && endDates.Count == 0)
        {
            endDates = [startDates[^1]];
            startDates = [startDates[0]];
        }

        if (startDates.Count == 0 || endDates.Count == 0)
        {
            var only = startDates.Count > 0 ? startDates[0] : endDates[0];
            startDates = [only];
            endDates = [only];
        }

        var startDay = startDates[0];
        var endDay = endDates[0];
        if (startDay.Year is < 1900 or > 2100 || endDay.Year is < 1900 or > 2100)
        {
            error = "date year must be between 1900 and 2100.";
            return false;
        }

        if (endDay < startDay)
        {
            error = "end must be on or after start.";
            return false;
        }

        if (endDay.DayNumber - startDay.DayNumber + 1 > 31)
        {
            error = "the window is at most 31 days.";
            return false;
        }

        resolvedStart = startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        resolvedEnd = endDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        error = string.Empty;
        return true;
    }

    private static List<DateOnly> ReadDates(string? raw)
    {
        var found = new List<DateOnly>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return found;
        }

        var trimmed = raw.Trim().Trim('"', '\'', '`', '\u201C', '\u201D', '\u2018', '\u2019');
        for (var i = 0; i + 10 <= trimmed.Length; i++)
        {
            if (trimmed[i + 4] != '-' || trimmed[i + 7] != '-')
            {
                continue;
            }

            if (DateOnly.TryParseExact(trimmed.Substring(i, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var embedded))
            {
                found.Add(embedded);
                i += 9;
            }
        }

        if (found.Count > 0)
        {
            return found;
        }

        if (trimmed.Length >= 8
            && trimmed[..8].All(char.IsDigit)
            && DateOnly.TryParseExact(trimmed[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
        {
            found.Add(compact);
            return found;
        }

        if (DateOnly.TryParseExact(
                trimmed,
                ["yyyy-MM-dd", "yyyy-M-d", "M/d/yyyy", "MM/dd/yyyy", "M-d-yyyy", "MM-dd-yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            found.Add(exact);
            return found;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime))
        {
            found.Add(DateOnly.FromDateTime(dateTime));
        }

        return found;
    }

    private static string WindowError(string? start, string? end) =>
        "start and end must contain a date, yyyy-MM-dd. Received start='"
        + Snip(start)
        + "' end='"
        + Snip(end)
        + "'.";

    private static string Snip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second;

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
