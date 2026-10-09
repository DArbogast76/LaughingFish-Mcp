namespace LaughingFish.Mcp.Clients;

public sealed record RiverStageApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IRiverStageApiClient
{
    Task<RiverStageApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken);
}
