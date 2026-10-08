using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaughingFish.Mcp.Clients;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Issued surf-zone height forecast for one point. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetSurfForecastTool
{
    public const string ToolName = "get_surf_forecast";
    public const string ToolDescription =
        "Returns the issued National Weather Service breaking-surf height for forecast zones intersecting a latitude and longitude. This is a surf-zone forecast, not a measured wave height and not offshore seas. Latitude is -90 to 90. Longitude is -180 to 180. Both are required. There is no place name, date, or radius. The map search uses 10 miles, then 25 miles only when the smaller search has no issued surf text. source beachSummary means locations has the map zones. source surfZoneForecast means the map search was empty and surfZoneForecast.rows has the text segment for this point's forecast zone; locations is empty in that case. text is the height as issued and is the forecast. minFeet and maxFeet are set only when text is one height or one simple range. Null means the office text could not be read that way, not that the surf is flat. beachname or zoneName is the zone, and the height covers that whole zone. period is the office label, not a swell period. An empty result means no issued surf forecast for the point. An empty result is not a failed call and not a statement that the water is calm.";

    private readonly ILogger<GetSurfForecastTool> _logger;
    private readonly ISurfForecastApiClient _client;

    public GetSurfForecastTool(ILogger<GetSurfForecastTool> logger, ISurfForecastApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetSurfForecastTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetSurfForecast tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude}",
            invocationId,
            context.Name,
            latitude,
            longitude);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetSurfForecast tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSurfForecast tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal async Task<JsonObject> ExecuteAsync(
        double? latitude,
        double? longitude,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (latitude is null || longitude is null
            || double.IsNaN(latitude.Value)
            || double.IsNaN(longitude.Value)
            || double.IsInfinity(latitude.Value)
            || double.IsInfinity(longitude.Value))
        {
            return Error("invalid_request", "latitude and longitude must be finite numbers.", invocationId);
        }

        if (latitude.Value is < -90 or > 90 || longitude.Value is < -180 or > 180)
        {
            return Error("invalid_request", "latitude must be between -90 and 90, longitude between -180 and 180.", invocationId);
        }

        var api = await _client.GetAsync(latitude.Value, longitude.Value, invocationId, cancellationToken)
            .ConfigureAwait(false);
        if (!api.IsSuccess || string.IsNullOrWhiteSpace(api.Body))
        {
            _logger.LogInformation(
                "GetSurfForecast API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                api.ErrorCode,
                api.StatusCode);
            return Error(api.ErrorCode ?? "surf_forecast_unavailable", api.ErrorMessage ?? "Surf Forecast API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetSurfForecast body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("surf_forecast_invalid_body", "Surf Forecast API returned a body that is not JSON.", invocationId);
        }

        var count = payload?["locations"] is JsonArray locations ? locations.Count : 0;
        _logger.LogInformation(
            "GetSurfForecast succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} Status={Status} LocationCount={LocationCount}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            payload?["status"]?.GetValue<string>(),
            count);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["surfForecast"] = payload
        };
    }

    private static JsonObject Error(string code, string message, string invocationId) => new()
    {
        ["ok"] = false,
        ["invocationId"] = invocationId,
        ["error"] = code,
        ["message"] = message
    };
}
