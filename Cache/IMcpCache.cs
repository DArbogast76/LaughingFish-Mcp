namespace LaughingFish.Mcp.Cache;

/// <summary>
/// First lookup in front of outbound service calls.
/// Implementations must not expose Redis or other stores to callers.
/// </summary>
public sealed record McpCacheLookup(bool Hit, string? Value);

public interface IMcpCache
{
    Task<McpCacheLookup> GetAsync(
        string key,
        string invocationId,
        CancellationToken cancellationToken);

    Task SetAsync(
        string key,
        string value,
        TimeSpan timeToLive,
        string invocationId,
        CancellationToken cancellationToken);

    Task<bool> PingAsync(
        string invocationId,
        CancellationToken cancellationToken);
}
