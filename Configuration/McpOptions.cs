namespace LaughingFish.Mcp.Configuration;

/// <summary>
/// Bound from environment / App Settings. No secrets live in code.
/// This slice only reports whether settings are present. It does not
/// connect to Redis, Maps, or the three APIs.
/// </summary>
public sealed class McpOptions
{
    public string RedisHost { get; set; } = string.Empty;
    public string RedisPort { get; set; } = "10000";
    public string RedisUser { get; set; } = string.Empty;
    public string AzureMapsClientId { get; set; } = string.Empty;
    public string AzureMapsEndpoint { get; set; } = string.Empty;
    public string WaterTempApiBaseUrl { get; set; } = string.Empty;
    public string WeatherApiBaseUrl { get; set; } = string.Empty;
    public string SunriseSunsetApiBaseUrl { get; set; } = string.Empty;
    public int PinRecentHours { get; set; } = 48;

    public bool RedisHostBound => !string.IsNullOrWhiteSpace(RedisHost);
    public bool RedisUserBound => !string.IsNullOrWhiteSpace(RedisUser);
    public bool AzureMapsBound =>
        !string.IsNullOrWhiteSpace(AzureMapsClientId)
        && !string.IsNullOrWhiteSpace(AzureMapsEndpoint);
    public bool WaterTempApiBound => !string.IsNullOrWhiteSpace(WaterTempApiBaseUrl);
    public bool WeatherApiBound => !string.IsNullOrWhiteSpace(WeatherApiBaseUrl);
    public bool SunriseSunsetApiBound => !string.IsNullOrWhiteSpace(SunriseSunsetApiBaseUrl);
}
