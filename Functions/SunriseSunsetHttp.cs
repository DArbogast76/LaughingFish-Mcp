using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_sunrise_sunset. Same payload as the MCP tool.
/// </summary>
public sealed class SunriseSunsetHttp
{
    public const string Route = "v1/tools/get-sunrise-sunset";

    private readonly ILogger<SunriseSunsetHttp> _logger;
    private readonly GetSunriseSunsetTool _tool;

    public SunriseSunsetHttp(ILogger<SunriseSunsetHttp> logger, GetSunriseSunsetTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetSunriseSunsetHttp")]
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
            var timeZone = ToolHttpRequest.ReadString(values, "timeZone");
            var latitude = ReadDouble(values, "latitude") ?? ReadDouble(values, "lat");
            var longitude = ReadDouble(values, "longitude") ?? ReadDouble(values, "lon");
            _logger.LogInformation(
                "GetSunriseSunset HTTP started. InvocationId={InvocationId} Method={Method} Lat={Lat} Lon={Lon} Date={Date} TimeZone={TimeZone}",
                invocationId,
                req.Method,
                latitude,
                longitude,
                date,
                timeZone);

            var result = await _tool.ExecuteAsync(
                latitude,
                longitude,
                date,
                timeZone,
                invocationId,
                context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetSunriseSunset HTTP finished. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetSunriseSunset HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                invocationId,
                error = "sunrise_sunset_failed",
                message = "Sunrise and sunset could not be read."
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
