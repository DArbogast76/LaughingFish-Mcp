namespace LaughingFish.Mcp.Clients;

public sealed record RiverForecastApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IRiverForecastApiClient
{
    Task<RiverForecastApiResult> GetAsync(
        double latitude,
        double longitude,
        int radiusMiles,
        string invocationId,
        CancellationToken cancellationToken);
}
