using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

public sealed class ChartedHazardsHttp
{
    public const string Route = "v1/tools/get-charted-hazards";

    private readonly ILogger<ChartedHazardsHttp> _logger;
    private readonly GetChartedHazardsTool _tool;

    public ChartedHazardsHttp(ILogger<ChartedHazardsHttp> logger, GetChartedHazardsTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetChartedHazardsHttp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = Route)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var latitude = ReadDouble(values, "latitude") ?? ReadDouble(values, "lat");
            var longitude = ReadDouble(values, "longitude") ?? ReadDouble(values, "lon");
            var radius = ReadInt(values, "radiusMiles");
            var limit = ReadInt(values, "limit");
            _logger.LogInformation(
                "GetChartedHazards HTTP started. InvocationId={InvocationId} Method={Method} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles} Limit={Limit}",
                invocationId,
                req.Method,
                latitude,
                longitude,
                radius,
                limit);

            var result = await _tool.ExecuteAsync(latitude, longitude, radius, limit, invocationId, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetChartedHazards HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetChartedHazards HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}", invocationId, started.ElapsedMilliseconds);
            return new ObjectResult(new JsonObject
            {
                ["ok"] = false,
                ["invocationId"] = invocationId,
                ["error"] = "charted_hazards_failed",
                ["message"] = "Charted hazards could not be read."
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        var raw = ToolHttpRequest.ReadString(values, key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        var raw = ToolHttpRequest.ReadString(values, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
