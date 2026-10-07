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
/// HTTP surface for get_tides. Same payload as the MCP tool.
/// </summary>
public sealed class TideNowHttp
{
    public const string Route = "v1/tools/get-tides";

    private readonly ILogger<TideNowHttp> _logger;
    private readonly GetTideNowTool _tool;

    public TideNowHttp(ILogger<TideNowHttp> logger, GetTideNowTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetTideNowHttp")]
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
                "GetTides HTTP started. InvocationId={InvocationId} Method={Method} Latitude={Latitude} Longitude={Longitude}",
                invocationId,
                req.Method,
                latitude,
                longitude);

            var result = await _tool.ExecuteAsync(latitude, longitude, invocationId, context.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetTides HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetTides HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new JsonObject
            {
                ["ok"] = false,
                ["invocationId"] = invocationId,
                ["error"] = "tide_now_failed",
                ["message"] = "Tide now could not be read."
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
