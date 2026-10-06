namespace LaughingFish.Mcp.Clients;

public sealed record ChartedHazardsApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface IChartedHazardsApiClient
{
    Task<ChartedHazardsApiResult> GetAsync(
        double latitude,
        double longitude,
        int radiusMiles,
        int limit,
        string invocationId,
        CancellationToken cancellationToken);
}
