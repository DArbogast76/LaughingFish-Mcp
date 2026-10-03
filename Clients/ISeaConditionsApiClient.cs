namespace LaughingFish.Mcp.Clients;

public sealed record SeaConditionsApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISeaConditionsApiClient
{
    Task<SeaConditionsApiResult> GetAsync(
        double latitude,
        double longitude,
        int nearest,
        int days,
        int maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken);
}
