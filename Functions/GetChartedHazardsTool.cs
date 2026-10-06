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
/// Charted wreck and obstruction points near one position. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetChartedHazardsTool
{
    public const string ToolName = "get_charted_hazards";
    public const string ToolDescription =
        "Returns charted wreck and obstruction points near a latitude and longitude. These are chart positions, not surveyed positions and not a forecast. Latitude is -90 to 90. Longitude is -180 to 180. Both are required. radiusMiles is an integer from 1 to 50 and defaults to 2. It is how far from the requested point the search looked, not the size of a wreck. limit is an integer from 1 to 100 and defaults to 25. Hazards are nearest first. If truncated is true, farther points inside the radius were left out and the returned hazards are the closest to the requested point. If truncated is false, every point inside the radius is included. kind is wreck or obstruction. distanceMiles and distanceMeters are the distance from the requested point to the charted point, computed for this call. leastDepthMeters is the charted sounding in meters. leastDepthFeet is that depth in feet. waterLevel says whether the chart shows the point covered or exposed. wreckCategory is the charted type, such as dangerous wreck, foul ground, crib, fish haven, or wellhead. An empty hazards list means no charted wreck or obstruction point was found in range. An empty list is not a failed call. chartCell is not useful in an answer.";

    private readonly ILogger<GetChartedHazardsTool> _logger;
    private readonly IChartedHazardsApiClient _client;

    public GetChartedHazardsTool(ILogger<GetChartedHazardsTool> logger, IChartedHazardsApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetChartedHazardsTool))]
    public Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        [McpToolProperty("radiusMiles", "Search radius in miles, an integer from 1 to 50. Defaults to 2. Not the size of a wreck.", false)] int? radiusMiles,
        [McpToolProperty("limit", "Maximum hazards to return, from 1 to 100. Defaults to 25. Nearest are kept.", false)] int? limit,
        FunctionContext functionContext)
    {
        _logger.LogInformation(
            "GetChartedHazards tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles} Limit={Limit}",
            functionContext.InvocationId,
            context.Name,
            latitude,
            longitude,
            radiusMiles,
            limit);
        return ExecuteAsync(latitude, longitude, radiusMiles, limit, functionContext.InvocationId, functionContext.CancellationToken);
    }

    public async Task<JsonObject> ExecuteAsync(
        double? latitude,
        double? longitude,
        int? radiusMiles,
        int? limit,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        if (latitude is null || longitude is null
            || double.IsNaN(latitude.Value) || double.IsNaN(longitude.Value)
            || double.IsInfinity(latitude.Value) || double.IsInfinity(longitude.Value))
        {
            return Error("invalid_request", "latitude and longitude must be finite numbers.", invocationId);
        }

        if (latitude.Value is < -90 or > 90 || longitude.Value is < -180 or > 180)
        {
            return Error("invalid_request", "latitude must be between -90 and 90, longitude between -180 and 180.", invocationId);
        }

        var radius = radiusMiles ?? 2;
        var take = limit ?? 25;
        if (radius < 1 || radius > 50)
        {
            return Error("invalid_request", "radiusMiles must be an integer from 1 to 50.", invocationId);
        }

        if (take < 1 || take > 100)
        {
            return Error("invalid_request", "limit must be an integer from 1 to 100.", invocationId);
        }

        var api = await _client.GetAsync(latitude.Value, longitude.Value, radius, take, invocationId, cancellationToken).ConfigureAwait(false);
        if (!api.IsSuccess || string.IsNullOrWhiteSpace(api.Body))
        {
            _logger.LogInformation(
                "GetChartedHazards API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                api.ErrorCode,
                api.StatusCode,
                started.ElapsedMilliseconds);
            return Error(api.ErrorCode ?? "charted_hazards_unavailable", api.ErrorMessage ?? "Wrecks API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetChartedHazards body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("charted_hazards_invalid_body", "Wrecks API returned a body that is not JSON.", invocationId);
        }

        var count = payload?["hazards"] is JsonArray hazards ? hazards.Count : 0;
        _logger.LogInformation(
            "GetChartedHazards succeeded. InvocationId={InvocationId} RadiusMiles={RadiusMiles} Limit={Limit} HazardCount={HazardCount} ElapsedMs={ElapsedMs}",
            invocationId,
            radius.ToString(CultureInfo.InvariantCulture),
            take.ToString(CultureInfo.InvariantCulture),
            count,
            started.ElapsedMilliseconds);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["chartedHazards"] = payload
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
