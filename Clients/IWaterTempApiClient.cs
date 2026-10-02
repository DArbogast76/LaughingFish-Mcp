namespace LaughingFish.Mcp.Clients;

public sealed record WaterTempApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    byte[]? ChartPng = null);

public interface IWaterTempApiClient
{
    Task<WaterTempApiResult> GetAsync(
        double latitude,
        double longitude,
        int nearest,
        int days,
        int maxDistanceMiles,
        bool includeChart,
        string invocationId,
        CancellationToken cancellationToken);
}
