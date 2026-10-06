namespace LaughingFish.Mcp.Configuration;

/// <summary>
/// Bound from environment / App Settings. No secrets live in code.
/// Integer settings keep int defaults. Invalid env values are ignored.
/// </summary>
public sealed class McpOptions
{
    public string RedisHost { get; set; } = string.Empty;
    public string RedisPort { get; set; } = "10000";
    public string RedisUser { get; set; } = string.Empty;
    public string AzureMapsClientId { get; set; } = string.Empty;
    public string AzureMapsEndpoint { get; set; } = string.Empty;
    public string WaterTempApiBaseUrl { get; set; } = string.Empty;
    public string WaterTempApiAudience { get; set; } = string.Empty;
    public string WeatherApiBaseUrl { get; set; } = string.Empty;
    public string WeatherApiAudience { get; set; } = string.Empty;
    public int WeatherApiTimeoutSeconds { get; set; } = 90;
    public string SunriseSunsetApiBaseUrl { get; set; } = string.Empty;
    public string SunriseSunsetApiAudience { get; set; } = string.Empty;
    public string SpeciesGuideApiBaseUrl { get; set; } = string.Empty;
    public string SpeciesGuideApiAudience { get; set; } = string.Empty;
    public string LunarCycleApiBaseUrl { get; set; } = string.Empty;
    public string LunarCycleApiAudience { get; set; } = string.Empty;
    public string TideApiBaseUrl { get; set; } = string.Empty;
    public string TideApiAudience { get; set; } = string.Empty;
    public string SeaConditionsApiBaseUrl { get; set; } = string.Empty;
    public string SeaConditionsApiAudience { get; set; } = string.Empty;
    public string UvApiBaseUrl { get; set; } = string.Empty;
    public string UvApiAudience { get; set; } = string.Empty;
    public string WeatherAlertsApiBaseUrl { get; set; } = string.Empty;
    public string WeatherAlertsApiAudience { get; set; } = string.Empty;
    public string SwimRiskApiBaseUrl { get; set; } = string.Empty;
    public string SwimRiskApiAudience { get; set; } = string.Empty;
    public string WrecksApiBaseUrl { get; set; } = string.Empty;
    public string WrecksApiAudience { get; set; } = string.Empty;
    public int PinRecentHours { get; set; } = 48;
    public int CacheDefaultTtlSeconds { get; set; } = 3600;
    public int WeatherCacheTtlSeconds { get; set; } = 3600;
    public int WaterTempCacheTtlSeconds { get; set; } = 3600;
    public int SunriseCacheTtlSeconds { get; set; } = 604800;
    public int MapsCacheTtlSeconds { get; set; } = 604800;
    public int SpeciesGuideCacheTtlSeconds { get; set; } = 604800;
    public int LunarCycleCacheTtlSeconds { get; set; } = 604800;
    public int TideCacheTtlSeconds { get; set; } = 604800;
    public int SeaConditionsCacheTtlSeconds { get; set; } = 3600;
    public int UvCacheTtlSeconds { get; set; } = 3600;
    public int WeatherAlertsCacheTtlSeconds { get; set; } = 300;
    public int SwimRiskCacheTtlSeconds { get; set; } = 3600;
    public int WrecksCacheTtlSeconds { get; set; } = 604800;

    public bool RedisHostBound => !string.IsNullOrWhiteSpace(RedisHost);
    public bool RedisUserBound => !string.IsNullOrWhiteSpace(RedisUser);
    public bool AzureMapsBound =>
        !string.IsNullOrWhiteSpace(AzureMapsClientId)
        && !string.IsNullOrWhiteSpace(AzureMapsEndpoint);
    public bool WaterTempApiBound => !string.IsNullOrWhiteSpace(WaterTempApiBaseUrl);
    public bool WaterTempApiAudienceBound => !string.IsNullOrWhiteSpace(WaterTempApiAudience);
    public bool WeatherApiBound => !string.IsNullOrWhiteSpace(WeatherApiBaseUrl);
    public bool WeatherApiAudienceBound => !string.IsNullOrWhiteSpace(WeatherApiAudience);
    public bool SunriseSunsetApiBound => !string.IsNullOrWhiteSpace(SunriseSunsetApiBaseUrl);
    public bool SunriseSunsetApiAudienceBound => !string.IsNullOrWhiteSpace(SunriseSunsetApiAudience);
    public bool SpeciesGuideApiBound => !string.IsNullOrWhiteSpace(SpeciesGuideApiBaseUrl);
    public bool SpeciesGuideApiAudienceBound => !string.IsNullOrWhiteSpace(SpeciesGuideApiAudience);
    public bool LunarCycleApiBound => !string.IsNullOrWhiteSpace(LunarCycleApiBaseUrl);
    public bool LunarCycleApiAudienceBound => !string.IsNullOrWhiteSpace(LunarCycleApiAudience);
    public bool TideApiBound => !string.IsNullOrWhiteSpace(TideApiBaseUrl);
    public bool TideApiAudienceBound => !string.IsNullOrWhiteSpace(TideApiAudience);
    public bool SeaConditionsApiBound => !string.IsNullOrWhiteSpace(SeaConditionsApiBaseUrl);
    public bool SeaConditionsApiAudienceBound => !string.IsNullOrWhiteSpace(SeaConditionsApiAudience);
    public bool UvApiBound => !string.IsNullOrWhiteSpace(UvApiBaseUrl);
    public bool UvApiAudienceBound => !string.IsNullOrWhiteSpace(UvApiAudience);
    public bool WeatherAlertsApiBound => !string.IsNullOrWhiteSpace(WeatherAlertsApiBaseUrl);
    public bool WeatherAlertsApiAudienceBound => !string.IsNullOrWhiteSpace(WeatherAlertsApiAudience);
    public bool SwimRiskApiBound => !string.IsNullOrWhiteSpace(SwimRiskApiBaseUrl);
    public bool SwimRiskApiAudienceBound => !string.IsNullOrWhiteSpace(SwimRiskApiAudience);
    public bool WrecksApiBound => !string.IsNullOrWhiteSpace(WrecksApiBaseUrl);
    public bool WrecksApiAudienceBound => !string.IsNullOrWhiteSpace(WrecksApiAudience);

    public TimeSpan WeatherCacheTtl => Ttl(WeatherCacheTtlSeconds, 3600);
    public TimeSpan WaterTempCacheTtl => Ttl(WaterTempCacheTtlSeconds, 3600);
    public TimeSpan SunriseCacheTtl => Ttl(SunriseCacheTtlSeconds, 604800);
    public TimeSpan MapsCacheTtl => Ttl(MapsCacheTtlSeconds, 604800);
    public TimeSpan SpeciesGuideCacheTtl => Ttl(SpeciesGuideCacheTtlSeconds, 604800);
    public TimeSpan LunarCycleCacheTtl => Ttl(LunarCycleCacheTtlSeconds, 604800);
    public TimeSpan TideCacheTtl => Ttl(TideCacheTtlSeconds, 604800);
    public TimeSpan SeaConditionsCacheTtl => Ttl(SeaConditionsCacheTtlSeconds, 3600);
    public TimeSpan UvCacheTtl => Ttl(UvCacheTtlSeconds, 3600);
    public TimeSpan WeatherAlertsCacheTtl => Ttl(WeatherAlertsCacheTtlSeconds, 300);
    public TimeSpan SwimRiskCacheTtl => Ttl(SwimRiskCacheTtlSeconds, 3600);
    public TimeSpan WrecksCacheTtl => Ttl(WrecksCacheTtlSeconds, 604800);
    public TimeSpan DefaultCacheTtl => Ttl(CacheDefaultTtlSeconds, 3600);

    private static TimeSpan Ttl(int seconds, int fallback) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : fallback);
}
