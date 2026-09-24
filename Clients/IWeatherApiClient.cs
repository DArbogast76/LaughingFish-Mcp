namespace LaughingFish.Mcp.Clients;

public sealed record WeatherApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    int? HoursReturned,
    int? HoursRequested);

public interface IWeatherApiClient
{
    Task<WeatherApiResult> GetForecastAsync(
        double latitude,
        double longitude,
        int hourCount,
        string invocationId,
        CancellationToken cancellationToken);
}
