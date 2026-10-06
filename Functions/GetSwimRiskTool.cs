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
/// Issued surf-zone rip current risk for one point. Does not geocode and does not name other tools.
/// </summary>
public sealed class GetSwimRiskTool
{
    public const string ToolName = "get_swim_risk";
    public const string ToolDescription =
        "Returns the issued National Weather Service surf-zone rip current risk for forecast zones intersecting a latitude and longitude. This is an issued forecast, not a measured observation and not a rating for one beach. Latitude is -90 to 90. Longitude is -180 to 180. Both are required. There is no place name, date, or radius. The search uses 10 miles, then 25 miles only when the smaller search has no rated zone. Each location is a forecast zone with the issuing office and a days array. Day 1 is 1200 UTC today through 1200 UTC tomorrow. Day 2 is the next 1200 UTC window. rip is Low, Moderate, or High. Low means life-threatening rip currents are unlikely but can still occur, especially near structures. Moderate means they are possible. High means they are likely. beachname is the zone, and the rating covers that whole zone. Product date and time are office text as issued, not UTC. An empty locations array means no rated zone within 25 miles. An empty array is not a failed call and not a statement that the water is safe.";

    private readonly ILogger<GetSwimRiskTool> _logger;
    private readonly ISwimRiskApiClient _client;

    public GetSwimRiskTool(ILogger<GetSwimRiskTool> logger, ISwimRiskApiClient client)
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetSwimRiskTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetSwimRisk tool started. InvocationId={InvocationId} Tool={Tool} Latitude={Latitude} Longitude={Longitude}",
            invocationId,
            context.Name,
            latitude,
            longitude);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetSwimRisk tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSwimRisk tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
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
                "GetSwimRisk API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                api.ErrorCode,
                api.StatusCode);
            return Error(api.ErrorCode ?? "swim_risk_unavailable", api.ErrorMessage ?? "Swim Risk API failed.", invocationId);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetSwimRisk body is not JSON. InvocationId={InvocationId}", invocationId);
            return Error("swim_risk_invalid_body", "Swim Risk API returned a body that is not JSON.", invocationId);
        }

        var count = payload?["locations"] is JsonArray locations ? locations.Count : 0;
        _logger.LogInformation(
            "GetSwimRisk succeeded. InvocationId={InvocationId} Latitude={Latitude} Longitude={Longitude} Status={Status} LocationCount={LocationCount}",
            invocationId,
            latitude.Value.ToString(CultureInfo.InvariantCulture),
            longitude.Value.ToString(CultureInfo.InvariantCulture),
            payload?["status"]?.GetValue<string>(),
            count);

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["swimRisk"] = payload
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
