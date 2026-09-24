namespace LaughingFish.Mcp.Cache;

/// <summary>
/// Cache that never hits and never writes. Used when Redis is unbound or cannot start.
/// Tools must keep working.
/// </summary>
public sealed class NoOpMcpCache : IMcpCache
{
    public Task<McpCacheLookup> GetAsync(
        string key,
        string invocationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new McpCacheLookup(false, null));

    public Task SetAsync(
        string key,
        string value,
        TimeSpan timeToLive,
        string invocationId,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<bool> PingAsync(
        string invocationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
