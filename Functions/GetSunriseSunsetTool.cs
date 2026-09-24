using System.Diagnostics;
using System.Globalization;
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
/// Proxies solar times from the SunriseSunset API.
/// Place names go through ILocationResolver (Azure Maps) so other tools can reuse the same path.
/// </summary>
public sealed class GetSunriseSunsetTool
{
    public const string ToolName = "get_sunrise_sunset";
    public const string ToolDescription =
        "Sunrise, sunset, dawn, dusk, civil twilight, and solar noon for a place and date. Pass place (preferred) or latitude and longitude. Convert relative dates such as 'tomorrow' or 'this Saturday' to yyyy-MM-dd before calling. Optional IANA time zone. Does not return weather or water temperature. Do not invent times when Maps or the API fails.";

    private readonly ILogger<GetSunriseSunsetTool> _logger;
    private readonly ISunriseSunsetApiClient _client;
    private readonly ILocationResolver _locations;
    private readonly McpOptions _options;

    public GetSunriseSunsetTool(
        ILogger<GetSunriseSunsetTool> logger,
        ISunriseSunsetApiClient client,
        ILocationResolver locations,
        IOptions<McpOptions> options)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
        _options = options.Value;
    }

    [Function(nameof(GetSunriseSunsetTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Example: Anchorage, Alaska. Preferred over raw coordinates.", false)] string? place,
        [McpToolProperty("latitude", "Latitude in decimal degrees when place is not provided.", false)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees when place is not provided.", false)] double? longitude,
        [McpToolProperty("date", "Calendar date yyyy-MM-dd. Convert 'this Saturday' to that format. Defaults to today's UTC date.", false)] string? date,
        [McpToolProperty("timeZone", "Optional IANA or Windows time zone id, for example America/Anchorage.", false)] string? timeZone,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "GetSunriseSunset tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Place={Place} Lat={Lat} Lon={Lon} Date={Date} TimeZone={TimeZone}",
            invocationId,
            context.Name,
            context.SessionId,
            place,
            latitude,
            longitude,
            date,
            timeZone);

        try
        {
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
                        "GetSunriseSunset tool location failed. InvocationId={InvocationId} Error={Error} ElapsedMs={ElapsedMs}",
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

            var resolvedDate = ResolveDate(date, out var dateError);
            if (resolvedDate is null)
            {
                _logger.LogInformation(
                    "GetSunriseSunset tool rejected date. InvocationId={InvocationId} DateRaw={DateRaw} Error={Error} ElapsedMs={ElapsedMs}",
                    invocationId,
                    date,
                    dateError,
                    started.ElapsedMilliseconds);
                return new
                {
                    ok = false,
                    error = "invalid_date",
                    message = dateError ?? "date must be yyyy-MM-dd.",
                    receivedDate = date,
                    invocationId
                };
            }

            var result = await _client.GetAsync(
                lat,
                lon,
                resolvedDate,
                string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim(),
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
                "GetSunriseSunset tool finished. InvocationId={InvocationId} Success={Success} StatusCode={StatusCode} Error={Error} ElapsedMs={ElapsedMs}",
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
                    sunriseSunsetApiBound = _options.SunriseSunsetApiBound,
                    body = payload
                };
            }

            return new
            {
                ok = true,
                invocationId,
                source = "LaughingFish.SunriseSunsetApi",
                statusCode = result.StatusCode,
                location = locationPayload ?? new { latitude = lat, longitude = lon },
                result = payload
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSunriseSunset tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-M-d",
        "M/d/yyyy",
        "MM/dd/yyyy"
    ];

    private static string? ResolveDate(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var trimmed = raw.Trim().Trim('"');
        if (trimmed.Length >= 10
            && trimmed[4] == '-'
            && DateOnly.TryParseExact(trimmed[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDay))
        {
            return FormatDate(isoDay, out error);
        }

        if (DateOnly.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return FormatDate(exact, out error);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return FormatDate(DateOnly.FromDateTime(dto.UtcDateTime), out error);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return FormatDate(DateOnly.FromDateTime(dt.ToUniversalTime()), out error);
        }

        error = "date must be yyyy-MM-dd.";
        return null;
    }

    private static string? FormatDate(DateOnly parsed, out string? error)
    {
        if (parsed.Year is < 1900 or > 2100)
        {
            error = "date year must be between 1900 and 2100.";
            return null;
        }

        error = null;
        return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static object Error(string code, string message, string invocationId) => new
    {
        ok = false,
        error = code,
        message,
        invocationId
    };
}
