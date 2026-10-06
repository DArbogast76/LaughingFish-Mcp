using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_lunar_cycle. Same payload as the MCP tool.
/// </summary>
public sealed class LunarCycleHttp
{
    public const string Route = "v1/tools/get-lunar-cycle";

    private readonly ILogger<LunarCycleHttp> _logger;
    private readonly GetLunarCycleTool _tool;

    public LunarCycleHttp(ILogger<LunarCycleHttp> logger, GetLunarCycleTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetLunarCycleHttp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = Route)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var date = ToolHttpRequest.ReadString(values, "date");
            var startDate = ToolHttpRequest.ReadString(values, "startDate");
            var endDate = ToolHttpRequest.ReadString(values, "endDate");
            var latitude = ReadDouble(values, "latitude") ?? ReadDouble(values, "lat");
            var longitude = ReadDouble(values, "longitude") ?? ReadDouble(values, "lon");

            _logger.LogInformation(
                "GetLunarCycle HTTP started. InvocationId={InvocationId} Method={Method} Lat={Lat} Lon={Lon} Date={Date} StartDate={StartDate} EndDate={EndDate}",
                invocationId,
                req.Method,
                latitude,
                longitude,
                date,
                startDate,
                endDate);

            var result = await _tool.ExecuteAsync(
                latitude,
                longitude,
                date,
                startDate,
                endDate,
                invocationId,
                context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "GetLunarCycle HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetLunarCycle HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                error = "lunar_cycle_unavailable",
                invocationId
            })
            {
                StatusCode = StatusCodes.Status502BadGateway
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
