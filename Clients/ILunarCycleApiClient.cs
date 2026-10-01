namespace LaughingFish.Mcp.Clients;

public sealed record LunarCycleApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ILunarCycleApiClient
{
    Task<LunarCycleApiResult> GetAsync(
        double latitude,
        double longitude,
        string startDate,
        string endDate,
        string invocationId,
        CancellationToken cancellationToken);
}
