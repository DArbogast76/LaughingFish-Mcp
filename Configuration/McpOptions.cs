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

    public TimeSpan WeatherCacheTtl => Ttl(WeatherCacheTtlSeconds, 3600);
    public TimeSpan WaterTempCacheTtl => Ttl(WaterTempCacheTtlSeconds, 3600);
    public TimeSpan SunriseCacheTtl => Ttl(SunriseCacheTtlSeconds, 604800);
    public TimeSpan MapsCacheTtl => Ttl(MapsCacheTtlSeconds, 604800);
    public TimeSpan DefaultCacheTtl => Ttl(CacheDefaultTtlSeconds, 3600);

    private static TimeSpan Ttl(int seconds, int fallback) =>
        TimeSpan.FromSeconds(seconds > 0 ? seconds : fallback);
}
