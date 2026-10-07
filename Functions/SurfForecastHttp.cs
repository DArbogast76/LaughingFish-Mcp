using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_surf_forecast. Same payload as the MCP tool.
/// </summary>
public sealed class SurfForecastHttp
{
    public const string Route = "v1/tools/get-surf-forecast";

    private readonly ILogger<SurfForecastHttp> _logger;
    private readonly GetSurfForecastTool _tool;

    public SurfForecastHttp(ILogger<SurfForecastHttp> logger, GetSurfForecastTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetSurfForecastHttp")]
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
            _logger.LogInformation(
                "GetSurfForecast HTTP started. InvocationId={InvocationId} Method={Method} Latitude={Latitude} Longitude={Longitude}",
                invocationId,
                req.Method,
                latitude,
                longitude);

            var result = await _tool.ExecuteAsync(latitude, longitude, invocationId, context.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetSurfForecast HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSurfForecast HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new JsonObject
            {
                ["ok"] = false,
                ["invocationId"] = invocationId,
                ["error"] = "surf_forecast_failed",
                ["message"] = "Surf forecast could not be read."
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
}
