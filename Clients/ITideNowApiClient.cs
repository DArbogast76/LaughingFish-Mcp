namespace LaughingFish.Mcp.Clients;

public sealed record TideNowApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ITideNowApiClient
{
    Task<TideNowApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken);
}
