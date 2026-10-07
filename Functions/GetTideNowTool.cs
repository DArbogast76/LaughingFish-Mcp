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
/// Latest measured water-surface height at the nearest gauge. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetTideNowTool
{
    public const string ToolName = "get_tides";
    public const string ToolDescription =
        "Latest measured water-surface height at the nearest CO-OPS gauge within 100 miles. Latitude and longitude are required. Do not pass a place name, a date, or a station id. heightFeet and heightMeters are the newest sample above the datum. MLLW is the chart zero for coastal and tidal-river gauges. LWD is the chart zero for Great Lakes gauges. coverage level_and_direction means direction is the change from the prior sample: rising, falling, or steady at 0.02 feet. coverage current_level_only means only one sample was returned, so direction is null. A null direction is not steady. status no_station_within_range means no gauge in range returned a sample. That is a successful result. It does not mean the water is flat. This is not the depth at the requested point, not the high and low schedule, and not the current. Do not invent a height or a direction.";

    private readonly ILogger<GetTideNowTool> _logger;
    private readonly ITideNowApiClient _client;

    public GetTideNowTool(ILogger<GetTideNowTool> logger, ITideNowApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetTideNowTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetTides tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude}",
            invocationId,
            context.Name,
            latitude,
            longitude);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetTides tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetTides tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
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
                "GetTides API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                api.ErrorCode,
                api.StatusCode);
            return Error(api.ErrorCode ?? "tide_now_unavailable", api.ErrorMessage ?? "Tide Now API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetTides body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("tide_now_invalid_body", "Tide Now API returned a body that is not JSON.", invocationId);
        }

        if (payload is JsonObject root)
        {
            root.Remove("stationId");
            root.Remove("stationName");
            if (root["observation"] is JsonObject observation)
            {
                observation.Remove("stationId");
                observation.Remove("stationName");
            }
        }

        _logger.LogInformation(
            "GetTides succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} Status={Status} Coverage={Coverage}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            payload?["status"]?.GetValue<string>(),
            payload?["coverage"]?.GetValue<string>());

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["tideNow"] = payload
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
