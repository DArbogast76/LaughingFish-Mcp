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
/// Active NWS alerts for one point. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetWeatherAlertsTool
{
    public const string ToolName = "get_weather_alerts";
    public const string ToolDescription =
        "Returns National Weather Service watches, warnings, and advisories active at the moment of the call for a latitude and longitude. This is a snapshot, not a forecast and not a history. Latitude is -90 to 90. Longitude is -180 to 180. Both are required. Each alert includes event, severity, urgency, certainty, response, headline, description, instruction, area description, effective, expires, and ends. Relay instruction when it is present. When instruction is empty, relay description. Do not invent safety guidance. Check expires before telling the user an alert is still in effect. expires is when the message expires. ends is when the hazard ends, when it was sent. An empty alerts array means no active alert contains that point, including points outside National Weather Service coverage. An empty array is not a failed call.";

    private readonly ILogger<GetWeatherAlertsTool> _logger;
    private readonly IWeatherAlertsApiClient _client;

    public GetWeatherAlertsTool(ILogger<GetWeatherAlertsTool> logger, IWeatherAlertsApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetWeatherAlertsTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetWeatherAlerts tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude}",
            invocationId,
            context.Name,
            latitude,
            longitude);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetWeatherAlerts tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetWeatherAlerts tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
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
                "GetWeatherAlerts API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                api.ErrorCode,
                api.StatusCode);
            return Error(api.ErrorCode ?? "weather_alerts_unavailable", api.ErrorMessage ?? "Weather Alerts API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetWeatherAlerts body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("weather_alerts_invalid_body", "Weather Alerts API returned a body that is not JSON.", invocationId);
        }

        var count = payload?["alerts"] is JsonArray alerts ? alerts.Count : 0;
        _logger.LogInformation(
            "GetWeatherAlerts succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} AlertCount={AlertCount}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            count);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["alerts"] = payload
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
