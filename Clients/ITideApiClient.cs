namespace LaughingFish.Mcp.Clients;

public sealed record TideApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ITideApiClient
{
    Task<TideApiResult> GetAsync(
        double latitude,
        double longitude,
        string start,
        string end,
        int nearest,
        int maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken);
}
