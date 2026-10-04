namespace LaughingFish.Mcp.Clients;

public sealed record WeatherAlertsApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IWeatherAlertsApiClient
{
    Task<WeatherAlertsApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken);
}
