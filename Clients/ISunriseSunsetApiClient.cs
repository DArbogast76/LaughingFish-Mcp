namespace LaughingFish.Mcp.Clients;

public sealed record SunriseSunsetApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISunriseSunsetApiClient
{
    Task<SunriseSunsetApiResult> GetAsync(
        double latitude,
        double longitude,
        string date,
        string? timeZone,
        string invocationId,
        CancellationToken cancellationToken);
}
