namespace LaughingFish.Mcp.Configuration;

/// <summary>
/// Bound from environment / App Settings. No secrets live in code.
/// Settings bind from App Settings. This host calls SunriseSunset, Weather,
/// and WaterTemp when those tools run. Redis is the MCP response cache only.
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
    public int PinRecentHours { get; set; } = 48;
    public int CacheDefaultTtlSeconds { get; set; } = 3600;
    public int WeatherCacheTtlSeconds { get; set; } = 3600;
    public int WaterTempCacheTtlSeconds { get; set; } = 3600;
    public int SunriseCacheTtlSeconds { get; set; } = 604800;
    public int MapsCacheTtlSeconds { get; set; } = 604800;

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

    public TimeSpan WeatherCacheTtl => Ttl(WeatherCacheTtlSeconds);
    public TimeSpan WaterTempCacheTtl => Ttl(WaterTempCacheTtlSeconds);
    public TimeSpan SunriseCacheTtl => Ttl(SunriseCacheTtlSeconds);
    public TimeSpan MapsCacheTtl => Ttl(MapsCacheTtlSeconds);
    public TimeSpan DefaultCacheTtl => Ttl(CacheDefaultTtlSeconds);

    private TimeSpan Ttl(int seconds) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : CacheDefaultTtlSeconds);
}
