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
/// Observed river stage at the nearest gauge. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetRiverStageTool
{
    public const string ToolName = "get_river_stage";
    public const string ToolDescription =
        "Observed river stage and discharge at the nearest live gauge on the same river, plus the next live gauges upstream on that mainstem. Latitude and longitude are required. Do not pass a place name, a date, or a site id. gauge is the nearest gauge with a reading in the last 48 hours. distanceMiles is from the requested point to that gauge. gaugeHeight is feet and meters above the gauge datum. discharge is cubic feet per second and cubic meters per second. direction is rising, falling, or steady from the recent window: 0.05 feet for stage, 5 percent for discharge. A null direction means the window had one sample. It does not mean steady. upstreamMainstem holds the next gauges on the same river that also have a reading in the last 48 hours, nearest first, usually two, or fewer if the reach runs out. Silent gauges are skipped and are not rows. outlook is rising_upstream, falling_upstream, mixed_upstream, or steady_upstream from those rows. status no_station_within_range means no live gauge was found. That is a successful result. It does not mean the river is flat. This is not a routed forecast, not an arrival time, and not the depth at the requested point. Do not invent a stage, a discharge, a direction, or an arrival time.";

    private readonly ILogger<GetRiverStageTool> _logger;
    private readonly IRiverStageApiClient _client;

    public GetRiverStageTool(ILogger<GetRiverStageTool> logger, IRiverStageApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetRiverStageTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetRiverStage tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude}",
            invocationId,
            context.Name,
            latitude,
            longitude);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetRiverStage tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetRiverStage tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
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
                "GetRiverStage API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                api.ErrorCode,
                api.StatusCode);
            return Error(api.ErrorCode ?? "river_stage_unavailable", api.ErrorMessage ?? "River Stage API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetRiverStage body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("river_stage_invalid_body", "River Stage API returned a body that is not JSON.", invocationId);
        }

        if (payload is JsonObject root)
        {
            StripSite(root);
            if (root["gauge"] is JsonObject gauge)
            {
                StripSite(gauge);
            }

            if (root["upstreamMainstem"] is JsonArray upstream)
            {
                foreach (var item in upstream)
                {
                    if (item is JsonObject row)
                    {
                        StripSite(row);
                    }
                }
            }
        }

        _logger.LogInformation(
            "GetRiverStage succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} Status={Status} Outlook={Outlook}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            payload?["status"]?.GetValue<string>(),
            payload?["outlook"]?.GetValue<string>());

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["riverStage"] = payload
        };
    }

    private static void StripSite(JsonObject node)
    {
        node.Remove("siteId");
        node.Remove("siteNo");
        node.Remove("stationId");
        node.Remove("usgsSite");
    }

    private static JsonObject Error(string code, string message, string invocationId) => new()
    {
        ["ok"] = false,
        ["invocationId"] = invocationId,
        ["error"] = code,
        ["message"] = message
    };
}
