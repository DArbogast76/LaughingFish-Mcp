using System.Diagnostics;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;

    public Health(ILogger<Health> logger, IOptions<McpOptions> options, IConfiguration configuration)
    {
        _logger = logger;
        _options = options.Value;
        _configuration = configuration;
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
                "Health bound settings. InvocationId={InvocationId} RedisHostBound={RedisHostBound} RedisPort={RedisPort} RedisUserBound={RedisUserBound} AzureMapsBound={AzureMapsBound} WaterTempApiBound={WaterTempApiBound} WeatherApiBound={WeatherApiBound} SunriseSunsetApiBound={SunriseSunsetApiBound} SpeciesGuideApiBound={SpeciesGuideApiBound} PinRecentHours={PinRecentHours}",
                invocationId,
                _options.RedisHostBound,
                _options.RedisPort,
                _options.RedisUserBound,
                _options.AzureMapsBound,
                _options.WaterTempApiBound,
                _options.WeatherApiBound,
                _options.SunriseSunsetApiBound,
                _options.SpeciesGuideApiBound,
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
                serverVersion = "0.4.18",
                transport = "streamable-http",
                endpoint = "/runtime/webhooks/mcp"
            },
            toolHttp = new
            {
                listSpecies = "/api/v1/tools/list-species",
                listGuideTopics = "/api/v1/tools/list-guide-topics",
                searchSpeciesGuides = "/api/v1/tools/search-species-guides",
                getChapter = "/api/v1/tools/get-chapter",
                getLunarCycle = "/api/v1/tools/get-lunar-cycle",
                getTidePredictions = "/api/v1/tools/get-tide-predictions",
                getSeaConditions = "/api/v1/tools/get-sea-conditions"
            },
            settings = new
            {
                redisHostBound = _options.RedisHostBound,
                redisPort = _options.RedisPort,
                redisUserBound = _options.RedisUserBound,
                azureMapsBound = _options.AzureMapsBound,
                waterTempApiBound = _options.WaterTempApiBound,
                waterTempApiAudienceBound = _options.WaterTempApiAudienceBound,
                weatherApiBound = _options.WeatherApiBound,
                weatherApiAudienceBound = _options.WeatherApiAudienceBound,
                sunriseSunsetApiBound = _options.SunriseSunsetApiBound,
                sunriseSunsetApiAudienceBound = _options.SunriseSunsetApiAudienceBound,
                speciesGuideApiBound = _options.SpeciesGuideApiBound,
                speciesGuideApiAudienceBound = _options.SpeciesGuideApiAudienceBound,
                speciesGuideSettingPresent = SettingPresent("SpeciesGuideApiBaseUrl"),
                speciesGuideBaseUrl = DescribeBaseUrl(),
                lunarCycleApiBound = _options.LunarCycleApiBound,
                lunarCycleApiAudienceBound = _options.LunarCycleApiAudienceBound,
                tideApiBound = _options.TideApiBound,
                tideApiAudienceBound = _options.TideApiAudienceBound,
                seaConditionsApiBound = _options.SeaConditionsApiBound,
                seaConditionsApiAudienceBound = _options.SeaConditionsApiAudienceBound,
                uvApiBound = _options.UvApiBound,
                uvApiAudienceBound = _options.UvApiAudienceBound,
                pinRecentHours = _options.PinRecentHours,
                weatherCacheTtlSeconds = (int)_options.WeatherCacheTtl.TotalSeconds,
                waterTempCacheTtlSeconds = (int)_options.WaterTempCacheTtl.TotalSeconds,
                sunriseCacheTtlSeconds = (int)_options.SunriseCacheTtl.TotalSeconds,
                mapsCacheTtlSeconds = (int)_options.MapsCacheTtl.TotalSeconds,
                speciesGuideCacheTtlSeconds = (int)_options.SpeciesGuideCacheTtl.TotalSeconds,
                lunarCycleCacheTtlSeconds = (int)_options.LunarCycleCacheTtl.TotalSeconds,
                tideCacheTtlSeconds = (int)_options.TideCacheTtl.TotalSeconds,
                seaConditionsCacheTtlSeconds = (int)_options.SeaConditionsCacheTtl.TotalSeconds,
                uvCacheTtlSeconds = (int)_options.UvCacheTtl.TotalSeconds,
                cacheDefaultTtlSeconds = (int)_options.DefaultCacheTtl.TotalSeconds
            }
        };
    }

    private bool SettingPresent(string key) =>
        !string.IsNullOrWhiteSpace(_configuration[key])
        || !string.IsNullOrWhiteSpace(_configuration[$"Values:{key}"])
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key));

    private object DescribeBaseUrl()
    {
        var raw = _configuration["SpeciesGuideApiBaseUrl"]
            ?? _configuration["Values:SpeciesGuideApiBaseUrl"]
            ?? Environment.GetEnvironmentVariable("SpeciesGuideApiBaseUrl")
            ?? string.Empty;
        var normalized = SpeciesGuideApiClient.NormalizeBaseUrl(raw);
        return new
        {
            length = raw.Length,
            firstChar = raw.Length == 0 ? 0 : (int)raw[0],
            lastChar = raw.Length == 0 ? 0 : (int)raw[^1],
            hasWhitespace = raw.Any(char.IsWhiteSpace),
            parseOk = normalized is not null
        };
    }
}
