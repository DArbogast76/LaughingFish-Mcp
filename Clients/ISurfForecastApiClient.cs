namespace LaughingFish.Mcp.Clients;

public sealed record SurfForecastApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISurfForecastApiClient
{
    Task<SurfForecastApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken);
}
