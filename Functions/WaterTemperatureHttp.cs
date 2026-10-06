using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_water_temperature. Same JSON payload as the MCP tool.
/// Does not stream a chart image.
/// </summary>
public sealed class WaterTemperatureHttp
{
    public const string Route = "v1/tools/get-water-temperature";

    private readonly ILogger<WaterTemperatureHttp> _logger;
    private readonly GetWaterTemperatureTool _tool;

    public WaterTemperatureHttp(ILogger<WaterTemperatureHttp> logger, GetWaterTemperatureTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetWaterTemperatureHttp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = Route)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var place = ToolHttpRequest.ReadString(values, "place");
            var latitude = ReadDouble(values, "latitude") ?? ReadDouble(values, "lat");
            var longitude = ReadDouble(values, "longitude") ?? ReadDouble(values, "lon");
            var nearest = ReadInt(values, "nearest");
            var days = ReadInt(values, "days");
            var maxDistanceMiles = ReadInt(values, "maxDistanceMiles");
            _logger.LogInformation(
                "GetWaterTemperature HTTP started. InvocationId={InvocationId} Method={Method} Place={Place} Lat={Lat} Lon={Lon} Nearest={Nearest} Days={Days} MaxDistanceMiles={MaxDistanceMiles}",
                invocationId,
                req.Method,
                place,
                latitude,
                longitude,
                nearest,
                days,
                maxDistanceMiles);

            var result = await _tool.ExecuteAsync(
                place,
                latitude,
                longitude,
                nearest,
                days,
                maxDistanceMiles,
                invocationId,
                context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetWaterTemperature HTTP finished. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetWaterTemperature HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                invocationId,
                error = "water_temperature_failed",
                message = "Water temperature could not be read."
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
