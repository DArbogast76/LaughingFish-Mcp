using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_tide_predictions. Same payload as the MCP tool.
/// </summary>
public sealed class TidePredictionsHttp
{
    public const string Route = "v1/tools/get-tide-predictions";

    private readonly ILogger<TidePredictionsHttp> _logger;
    private readonly GetTidePredictionsTool _tool;

    public TidePredictionsHttp(ILogger<TidePredictionsHttp> logger, GetTidePredictionsTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetTidePredictionsHttp")]
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
            var start = ToolHttpRequest.ReadString(values, "start") ?? ToolHttpRequest.ReadString(values, "startDate");
            var end = ToolHttpRequest.ReadString(values, "end") ?? ToolHttpRequest.ReadString(values, "endDate");
            var latitude = ReadDouble(values, "latitude") ?? ReadDouble(values, "lat");
            var longitude = ReadDouble(values, "longitude") ?? ReadDouble(values, "lon");
            var nearest = ReadInt(values, "nearest");
            var maxDistanceMiles = ReadInt(values, "maxDistanceMiles");

            _logger.LogInformation(
                "GetTidePredictions HTTP started. InvocationId={InvocationId} Method={Method} Place={Place} Lat={Lat} Lon={Lon} Start={Start} End={End} Nearest={Nearest} MaxDistanceMiles={MaxDistanceMiles}",
                invocationId,
                req.Method,
                place,
                latitude,
                longitude,
                start,
                end,
                nearest,
                maxDistanceMiles);

            var result = await _tool.ExecuteAsync(
                place,
                latitude,
                longitude,
                start,
                end,
                null,
                null,
                nearest,
                maxDistanceMiles,
                invocationId,
                context.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "GetTidePredictions HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetTidePredictions HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                error = "tide_predictions_unavailable",
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
