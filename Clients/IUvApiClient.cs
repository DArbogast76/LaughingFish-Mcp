namespace LaughingFish.Mcp.Clients;

public sealed record UvApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IUvApiClient
{
    Task<UvApiResult> GetAsync(
        string zip,
        string invocationId,
        CancellationToken cancellationToken);
}
