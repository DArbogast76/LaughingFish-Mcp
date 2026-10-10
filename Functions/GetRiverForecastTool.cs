using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using LaughingFish.Mcp.Clients;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace LaughingFish.Mcp.Functions;

public sealed class GetRiverForecastTool
{
    public const string ToolName = "get_river_forecast";

    public const string ToolDescription =
        "Ten-day modeled streamflow forecast for the nearest National Water Model reach linked to a gauge near the point. Latitude and longitude are required. radiusMiles is optional and must be 10, 25, or 50; omit it for 25. Do not pass a place name, a date, or a reach id. reach is the nearest gauge with a linked reach inside the radius. distanceMiles is from the requested point to that gauge. source is the ensemble mean of the 6-member medium-range suite, or the precomputed mean when the full ensemble is empty. referenceTime is the model run time. daily has ten calendar days starting from the current UTC date, including today. mean is the average flow for that day. peak is the highest value that day. hourly is the first 72 hours of the mean series. Every flow value is returned in cubic feet per second and cubic meters per second. status no_reach_within_range means no linked reach was inside the radius. status no_forecast_for_reach means a reach was found but the series was empty. Both are successful results. This is model guidance, not a measured gauge reading, not an official River Forecast Center forecast, and not an arrival time. Do not invent a flow, a peak, or a flood stage.";

    private readonly ILogger<GetRiverForecastTool> _logger;
    private readonly IRiverForecastApiClient _client;

    public GetRiverForecastTool(ILogger<GetRiverForecastTool> logger, IRiverForecastApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetRiverForecastTool))]
    public Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        [McpToolProperty("radiusMiles", "Search radius in miles. 10, 25, or 50. Omit for 25.", false)] int? radiusMiles,
        FunctionContext functionContext)
    {
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetRiverForecast tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles}",
            invocationId,
            context.Name,
            latitude,
            longitude,
            radiusMiles);
        return ExecuteAsync(latitude, longitude, radiusMiles, invocationId, functionContext.CancellationToken);
    }

    public async Task<JsonObject> ExecuteAsync(
        double? latitude,
        double? longitude,
        int? radiusMiles,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        if (latitude is null || longitude is null || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            _logger.LogInformation(
                "GetRiverForecast rejected. InvocationId={InvocationId} Reason=invalid_point ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return Error("invalid_request", "latitude and longitude are required and must be in range.", invocationId);
        }

        var radius = radiusMiles ?? 25;
        if (radius is not (10 or 25 or 50))
        {
            _logger.LogInformation(
                "GetRiverForecast rejected. InvocationId={InvocationId} Reason=invalid_radius RadiusMiles={RadiusMiles} ElapsedMs={ElapsedMs}",
                invocationId,
                radius,
                started.ElapsedMilliseconds);
            return Error("invalid_request", "radiusMiles must be 10, 25, or 50.", invocationId);
        }

        var api = await _client.GetAsync(latitude.Value, longitude.Value, radius, invocationId, cancellationToken)
            .ConfigureAwait(false);
        if (!api.IsSuccess || string.IsNullOrWhiteSpace(api.Body))
        {
            _logger.LogInformation(
                "GetRiverForecast API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                api.ErrorCode,
                api.StatusCode,
                started.ElapsedMilliseconds);
            return Error(api.ErrorCode ?? "river_forecast_unavailable", api.ErrorMessage ?? "River Forecast API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "GetRiverForecast body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("river_forecast_invalid_body", "River Forecast API returned a body that is not JSON.", invocationId);
        }

        if (payload is JsonObject root && root["reach"] is JsonObject reach)
        {
            reach.Remove("reachId");
        }

        _logger.LogInformation(
            "GetRiverForecast succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles} Status={Status} ElapsedMs={ElapsedMs}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            radius,
            payload?["status"]?.GetValue<string>(),
            started.ElapsedMilliseconds);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["riverForecast"] = payload
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
