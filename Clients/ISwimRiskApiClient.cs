namespace LaughingFish.Mcp.Clients;

public sealed record SwimRiskApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISwimRiskApiClient
{
    Task<SwimRiskApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken);
}
