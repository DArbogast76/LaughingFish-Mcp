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
/// Proxies NWS forecast hours from the Weather API.
/// Place names go through ILocationResolver. Hours are trimmed here; the API is unchanged.
/// </summary>
public sealed class GetWeatherForecastTool
{
    public const string ToolName = "get_weather_forecast";
    public const string ToolDescription =
        "Returns an hourly weather forecast. Pass place (city or address) or latitude+longitude. Optional hours (24, 48, or 72) or days (1, 3, or 7). Default is 24 hours. Do not invent forecasts when Maps or the API fails.";

    private static readonly int[] AllowedHours = [24, 48, 72];
    private static readonly int[] AllowedDays = [1, 3, 7];

    private readonly ILogger<GetWeatherForecastTool> _logger;
    private readonly IWeatherApiClient _client;
    private readonly ILocationResolver _locations;
    private readonly McpOptions _options;

    public GetWeatherForecastTool(
        ILogger<GetWeatherForecastTool> logger,
        IWeatherApiClient client,
        ILocationResolver locations,
        IOptions<McpOptions> options)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
        _options = options.Value;
    }

    [Function(nameof(GetWeatherForecastTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Example: Annapolis, Maryland. Preferred over raw coordinates.", false)] string? place,
        [McpToolProperty("latitude", "Latitude in decimal degrees when place is not provided.", false)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees when place is not provided.", false)] double? longitude,
        [McpToolProperty("hours", "Optional forecast length in hours. Allowed: 24, 48, 72. Do not send with days.", false)] int? hours,
        [McpToolProperty("days", "Optional forecast length in days. Allowed: 1, 3, 7. Do not send with hours.", false)] int? days,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "GetWeatherForecast tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Place={Place} Lat={Lat} Lon={Lon} Hours={Hours} Days={Days}",
            invocationId,
            context.Name,
            context.SessionId,
            place,
            latitude,
            longitude,
            hours,
            days);

        try
        {
            if (!TryResolveHourCount(hours, days, out var hourCount, out var windowError))
            {
                _logger.LogInformation(
                    "GetWeatherForecast tool rejected window. InvocationId={InvocationId} Hours={Hours} Days={Days} Error={Error} ElapsedMs={ElapsedMs}",
                    invocationId,
                    hours,
                    days,
                    windowError,
                    started.ElapsedMilliseconds);
                return Error(windowError ?? "invalid_window", "hours must be 24, 48, or 72. days must be 1, 3, or 7. Do not send both.", invocationId);
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
                        "GetWeatherForecast tool location failed. InvocationId={InvocationId} Error={Error} ElapsedMs={ElapsedMs}",
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

            var result = await _client.GetForecastAsync(
                lat,
                lon,
                hourCount,
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
                "GetWeatherForecast tool finished. InvocationId={InvocationId} Success={Success} StatusCode={StatusCode} Error={Error} HoursRequested={HoursRequested} HoursReturned={HoursReturned} ElapsedMs={ElapsedMs}",
                invocationId,
                result.IsSuccess,
                result.StatusCode,
                result.ErrorCode,
                result.HoursRequested,
                result.HoursReturned,
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
                    weatherApiBound = _options.WeatherApiBound,
                    weatherApiAudienceBound = _options.WeatherApiAudienceBound,
                    body = payload
                };
            }

            return new
            {
                ok = true,
                invocationId,
                source = "LaughingFish.WeatherApi",
                statusCode = result.StatusCode,
                hoursRequested = result.HoursRequested,
                hoursReturned = result.HoursReturned,
                location = locationPayload ?? new { latitude = lat, longitude = lon },
                result = payload
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetWeatherForecast tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal static bool TryResolveHourCount(int? hours, int? days, out int hourCount, out string? error)
    {
        hourCount = 24;
        error = null;

        if (hours is not null && days is not null)
        {
            error = "invalid_window";
            return false;
        }

        if (hours is { } h)
        {
            if (!AllowedHours.Contains(h))
            {
                error = "invalid_window";
                return false;
            }

            hourCount = h;
            return true;
        }

        if (days is { } d)
        {
            if (!AllowedDays.Contains(d))
            {
                error = "invalid_window";
                return false;
            }

            hourCount = d * 24;
            return true;
        }

        return true;
    }

    private static object Error(string code, string message, string invocationId) => new
    {
        ok = false,
        error = code,
        message,
        invocationId
    };
}
