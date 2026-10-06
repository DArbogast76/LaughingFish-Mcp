using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_weather_forecast. Same payload as the MCP tool.
/// </summary>
public sealed class WeatherForecastHttp
{
    public const string Route = "v1/tools/get-weather-forecast";

    private readonly ILogger<WeatherForecastHttp> _logger;
    private readonly GetWeatherForecastTool _tool;

    public WeatherForecastHttp(ILogger<WeatherForecastHttp> logger, GetWeatherForecastTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetWeatherForecastHttp")]
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
            var hours = ReadInt(values, "hours");
            var days = ReadInt(values, "days");
            _logger.LogInformation(
                "GetWeatherForecast HTTP started. InvocationId={InvocationId} Method={Method} Place={Place} Lat={Lat} Lon={Lon} Hours={Hours} Days={Days}",
                invocationId,
                req.Method,
                place,
                latitude,
                longitude,
                hours,
                days);

            var result = await _tool.ExecuteAsync(
                place,
                latitude,
                longitude,
                hours,
                days,
                invocationId,
                context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetWeatherForecast HTTP finished. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetWeatherForecast HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                invocationId,
                error = "weather_forecast_failed",
                message = "The weather forecast could not be read."
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
