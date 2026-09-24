using System.Diagnostics;
using LaughingFish.Mcp.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp;

/// <summary>
/// Deployment smoke test. Does not call Redis, Maps, or downstream APIs.
/// GET /api/health
/// </summary>
public sealed class Health
{
    private readonly ILogger<Health> _logger;
    private readonly McpOptions _options;

    public Health(ILogger<Health> logger, IOptions<McpOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    [Function("Health")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;

        _logger.LogInformation(
            "Health started. InvocationId={InvocationId} Method={Method} Path={Path}",
            invocationId,
            req.Method,
            req.Path.Value);

        try
        {
            var payload = BuildHealthPayload(invocationId);

            _logger.LogInformation(
                "Health bound settings. InvocationId={InvocationId} RedisHostBound={RedisHostBound} RedisPort={RedisPort} RedisUserBound={RedisUserBound} AzureMapsBound={AzureMapsBound} WaterTempApiBound={WaterTempApiBound} WeatherApiBound={WeatherApiBound} SunriseSunsetApiBound={SunriseSunsetApiBound} PinRecentHours={PinRecentHours}",
                invocationId,
                _options.RedisHostBound,
                _options.RedisPort,
                _options.RedisUserBound,
                _options.AzureMapsBound,
                _options.WaterTempApiBound,
                _options.WeatherApiBound,
                _options.SunriseSunsetApiBound,
                _options.PinRecentHours);

            _logger.LogInformation(
                "Health succeeded. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                StatusCodes.Status200OK,
                started.ElapsedMilliseconds);

            return new OkObjectResult(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Health failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal object BuildHealthPayload(string invocationId)
    {
        return new
        {
            status = "ok",
            utc = DateTime.UtcNow.ToString("o"),
            invocationId,
            worker = "dotnet-isolated",
            targetFramework = "net10.0",
            app = "LaughingFish.Mcp",
            mcp = new
            {
                serverName = "LaughingFish-Mcp",
                serverVersion = "0.3.0",
                transport = "streamable-http",
                endpoint = "/runtime/webhooks/mcp"
            },
            settings = new
            {
                redisHostBound = _options.RedisHostBound,
                redisPort = _options.RedisPort,
                redisUserBound = _options.RedisUserBound,
                azureMapsBound = _options.AzureMapsBound,
                waterTempApiBound = _options.WaterTempApiBound,
                weatherApiBound = _options.WeatherApiBound,
                sunriseSunsetApiBound = _options.SunriseSunsetApiBound,
                sunriseSunsetApiAudienceBound = _options.SunriseSunsetApiAudienceBound,
                pinRecentHours = _options.PinRecentHours
            }
        };
    }
}
