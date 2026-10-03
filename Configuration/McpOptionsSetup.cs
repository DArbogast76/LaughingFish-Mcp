using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Configuration;

/// <summary>
/// Maps App Settings onto McpOptions. Integer keys are parsed;
/// invalid values are ignored and the property default remains.
/// Does not use ConfigurationBinder for ints (that throws).
/// </summary>
public sealed class McpOptionsSetup : IConfigureOptions<McpOptions>
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpOptionsSetup> _logger;

    public McpOptionsSetup(IConfiguration configuration, ILogger<McpOptionsSetup> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public void Configure(McpOptions options)
    {
        options.RedisHost = ReadString("RedisHost", options.RedisHost);
        options.RedisPort = ReadString("RedisPort", options.RedisPort);
        options.RedisUser = ReadString("RedisUser", options.RedisUser);
        options.AzureMapsClientId = ReadString("AzureMapsClientId", options.AzureMapsClientId);
        options.AzureMapsEndpoint = ReadString("AzureMapsEndpoint", options.AzureMapsEndpoint);
        options.WaterTempApiBaseUrl = ReadString("WaterTempApiBaseUrl", options.WaterTempApiBaseUrl);
        options.WaterTempApiAudience = ReadString("WaterTempApiAudience", options.WaterTempApiAudience);
        options.WeatherApiBaseUrl = ReadString("WeatherApiBaseUrl", options.WeatherApiBaseUrl);
        options.WeatherApiAudience = ReadString("WeatherApiAudience", options.WeatherApiAudience);
        options.SunriseSunsetApiBaseUrl = ReadString("SunriseSunsetApiBaseUrl", options.SunriseSunsetApiBaseUrl);
        options.SunriseSunsetApiAudience = ReadString("SunriseSunsetApiAudience", options.SunriseSunsetApiAudience);
        options.SpeciesGuideApiBaseUrl = ReadString("SpeciesGuideApiBaseUrl", options.SpeciesGuideApiBaseUrl);
        options.SpeciesGuideApiAudience = ReadString("SpeciesGuideApiAudience", options.SpeciesGuideApiAudience);
        options.LunarCycleApiBaseUrl = ReadString("LunarCycleApiBaseUrl", options.LunarCycleApiBaseUrl);
        options.LunarCycleApiAudience = ReadString("LunarCycleApiAudience", options.LunarCycleApiAudience);
        options.TideApiBaseUrl = ReadString("TideApiBaseUrl", options.TideApiBaseUrl);
        options.TideApiAudience = ReadString("TideApiAudience", options.TideApiAudience);
        options.SeaConditionsApiBaseUrl = ReadString("SeaConditionsApiBaseUrl", options.SeaConditionsApiBaseUrl);
        options.SeaConditionsApiAudience = ReadString("SeaConditionsApiAudience", options.SeaConditionsApiAudience);

        options.WeatherApiTimeoutSeconds = ReadInt("WeatherApiTimeoutSeconds", options.WeatherApiTimeoutSeconds);
        options.PinRecentHours = ReadInt("PinRecentHours", options.PinRecentHours);
        options.CacheDefaultTtlSeconds = ReadInt("CacheDefaultTtlSeconds", options.CacheDefaultTtlSeconds);
        options.WeatherCacheTtlSeconds = ReadInt("WeatherCacheTtlSeconds", options.WeatherCacheTtlSeconds);
        options.WaterTempCacheTtlSeconds = ReadInt("WaterTempCacheTtlSeconds", options.WaterTempCacheTtlSeconds);
        options.SunriseCacheTtlSeconds = ReadInt("SunriseCacheTtlSeconds", options.SunriseCacheTtlSeconds);
        options.MapsCacheTtlSeconds = ReadInt("MapsCacheTtlSeconds", options.MapsCacheTtlSeconds);
        options.SpeciesGuideCacheTtlSeconds = ReadInt("SpeciesGuideCacheTtlSeconds", options.SpeciesGuideCacheTtlSeconds);
        options.LunarCycleCacheTtlSeconds = ReadInt("LunarCycleCacheTtlSeconds", options.LunarCycleCacheTtlSeconds);
        options.TideCacheTtlSeconds = ReadInt("TideCacheTtlSeconds", options.TideCacheTtlSeconds);
        options.SeaConditionsCacheTtlSeconds = ReadInt("SeaConditionsCacheTtlSeconds", options.SeaConditionsCacheTtlSeconds);

        _logger.LogInformation(
            "MCP options bound. WaterTempApiBound={WaterTempApiBound} WeatherApiBound={WeatherApiBound} SunriseSunsetApiBound={SunriseSunsetApiBound} SpeciesGuideApiBound={SpeciesGuideApiBound} LunarCycleApiBound={LunarCycleApiBound} TideApiBound={TideApiBound} TideApiAudienceBound={TideApiAudienceBound} SeaConditionsApiBound={SeaConditionsApiBound} SeaConditionsApiAudienceBound={SeaConditionsApiAudienceBound} SeaConditionsCacheTtlSeconds={SeaConditionsCacheTtlSeconds}",
            options.WaterTempApiBound,
            options.WeatherApiBound,
            options.SunriseSunsetApiBound,
            options.SpeciesGuideApiBound,
            options.LunarCycleApiBound,
            options.TideApiBound,
            options.TideApiAudienceBound,
            options.SeaConditionsApiBound,
            options.SeaConditionsApiAudienceBound,
            options.SeaConditionsCacheTtlSeconds);
    }

    private string ReadString(string key, string fallback)
    {
        var raw = FirstNonEmpty(
            _configuration[key],
            _configuration[$"Values:{key}"],
            Environment.GetEnvironmentVariable(key));

        return raw ?? fallback;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private int ReadInt(string key, int fallback)
    {
        var raw = _configuration[key];
        if (raw is null)
        {
            return fallback;
        }

        if (int.TryParse(raw.Trim(), out var parsed) && parsed > 0)
        {
            return parsed;
        }

        _logger.LogWarning(
            "Ignoring invalid integer App Setting {Key} ValueLength={ValueLength}. Using default {Default}.",
            key,
            raw.Trim().Length,
            fallback);
        return fallback;
    }
}
