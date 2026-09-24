namespace LaughingFish.Mcp.Clients;

public sealed record WaterTempApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IWaterTempApiClient
{
    Task<WaterTempApiResult> GetAsync(
        double latitude,
        double longitude,
        int nearest,
        int days,
        int maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken);
}
