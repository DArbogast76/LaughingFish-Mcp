using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

public sealed class RiverForecastHttp
{
    public const string Route = "v1/tools/get-river-forecast";

    private readonly GetRiverForecastTool _tool;
    private readonly ILogger<RiverForecastHttp> _logger;

    public RiverForecastHttp(GetRiverForecastTool tool, ILogger<RiverForecastHttp> logger)
    {
        _tool = tool;
        _logger = logger;
    }

    [Function("GetRiverForecastHttp")]
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
            var radius = ReadInt(values, "radiusMiles") ?? ReadInt(values, "radius");
            _logger.LogInformation(
                "GetRiverForecast HTTP started. InvocationId={InvocationId} Method={Method} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles}",
                invocationId,
                req.Method,
                latitude,
                longitude,
                radius);

            var result = await _tool.ExecuteAsync(latitude, longitude, radius, invocationId, context.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetRiverForecast HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetRiverForecast HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new JsonObject
            {
                ["ok"] = false,
                ["invocationId"] = invocationId,
                ["error"] = "river_forecast_failed",
                ["message"] = "River forecast could not be read."
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        var raw = ToolHttpRequest.ReadString(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        var raw = ToolHttpRequest.ReadString(values, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
