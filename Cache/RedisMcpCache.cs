using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Configuration;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LaughingFish.Mcp.Cache;

/// <summary>
/// Redis-backed IMcpCache. Entra token only — no access keys.
/// When Redis host is unbound, Get is always a miss and Set is a no-op.
/// Failures never throw to callers; they become misses / skipped writes.
/// </summary>
public sealed class RedisMcpCache : IMcpCache, IAsyncDisposable
{
    private const int ConnectTimeoutMilliseconds = 5000;
    private const int CommandTimeoutMilliseconds = 5000;

    private readonly ILogger<RedisMcpCache> _logger;
    private readonly McpOptions _options;
    private readonly TokenCredential _credential = CreateCredential();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private ConnectionMultiplexer? _mux;
    private IDatabase? _db;
    private bool _unbound;

    public RedisMcpCache(ILogger<RedisMcpCache> logger, IOptions<McpOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _unbound = !_options.RedisHostBound;
        if (_unbound)
        {
            _logger.LogInformation("MCP cache unbound. RedisHost is not configured. Get is miss; Set is skipped.");
        }
    }

    public async Task<McpCacheLookup> GetAsync(
        string key,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || _unbound)
        {
            return new McpCacheLookup(false, null);
        }

        var started = Stopwatch.StartNew();
        try
        {
            var db = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (db is null)
            {
                _logger.LogWarning(
                    "MCP cache get skipped. InvocationId={InvocationId} Key={Key} Reason=not_connected ElapsedMs={ElapsedMs}",
                    invocationId,
                    key,
                    started.ElapsedMilliseconds);
                return new McpCacheLookup(false, null);
            }

            var value = await db.StringGetAsync(key).ConfigureAwait(false);
            var hit = value.HasValue;
            _logger.LogInformation(
                "MCP cache get. InvocationId={InvocationId} Key={Key} Hit={Hit} ValueLength={ValueLength} ElapsedMs={ElapsedMs}",
                invocationId,
                key,
                hit,
                hit ? value.ToString().Length : 0,
                started.ElapsedMilliseconds);
            return hit
                ? new McpCacheLookup(true, value.ToString())
                : new McpCacheLookup(false, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MCP cache get failed. InvocationId={InvocationId} Key={Key} ElapsedMs={ElapsedMs}",
                invocationId,
                key,
                started.ElapsedMilliseconds);
            return new McpCacheLookup(false, null);
        }
    }

    public async Task SetAsync(
        string key,
        string value,
        TimeSpan timeToLive,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || value is null || _unbound)
        {
            return;
        }

        if (timeToLive <= TimeSpan.Zero)
        {
            _logger.LogWarning(
                "MCP cache set skipped. InvocationId={InvocationId} Key={Key} Reason=invalid_ttl",
                invocationId,
                key);
            return;
        }

        var started = Stopwatch.StartNew();
        try
        {
            var db = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (db is null)
            {
                _logger.LogWarning(
                    "MCP cache set skipped. InvocationId={InvocationId} Key={Key} Reason=not_connected ElapsedMs={ElapsedMs}",
                    invocationId,
                    key,
                    started.ElapsedMilliseconds);
                return;
            }

            await db.StringSetAsync(key, value, timeToLive).ConfigureAwait(false);
            _logger.LogInformation(
                "MCP cache set. InvocationId={InvocationId} Key={Key} TtlSeconds={TtlSeconds} ValueLength={ValueLength} ElapsedMs={ElapsedMs}",
                invocationId,
                key,
                (int)timeToLive.TotalSeconds,
                value.Length,
                started.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MCP cache set failed. InvocationId={InvocationId} Key={Key} ElapsedMs={ElapsedMs}",
                invocationId,
                key,
                started.ElapsedMilliseconds);
        }
    }

    public async Task<bool> PingAsync(string invocationId, CancellationToken cancellationToken)
    {
        if (_unbound)
        {
            _logger.LogInformation(
                "MCP cache ping skipped. InvocationId={InvocationId} Reason=unbound",
                invocationId);
            return false;
        }

        var started = Stopwatch.StartNew();
        try
        {
            var db = await GetDatabaseAsync(cancellationToken).ConfigureAwait(false);
            if (db is null)
            {
                _logger.LogWarning(
                    "MCP cache ping failed. InvocationId={InvocationId} Reason=not_connected ElapsedMs={ElapsedMs}",
                    invocationId,
                    started.ElapsedMilliseconds);
                return false;
            }

            await db.PingAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "MCP cache ping succeeded. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MCP cache ping failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return false;
        }
    }

    private async Task<IDatabase?> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_db is not null)
        {
            return _db;
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_db is not null)
            {
                return _db;
            }

            if (!_options.RedisHostBound)
            {
                _unbound = true;
                return null;
            }

            var host = _options.RedisHost.Trim();
            if (!int.TryParse(_options.RedisPort, out var port) || port <= 0)
            {
                port = 10000;
            }

            var configuration = ConfigurationOptions.Parse($"{host}:{port}");
            configuration.Ssl = true;
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = ConnectTimeoutMilliseconds;
            configuration.SyncTimeout = CommandTimeoutMilliseconds;
            configuration.AsyncTimeout = CommandTimeoutMilliseconds;
            if (!string.IsNullOrWhiteSpace(_options.RedisUser))
            {
                configuration.User = _options.RedisUser.Trim();
            }

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(ConnectTimeoutMilliseconds);

            await configuration.ConfigureForAzureWithTokenCredentialAsync(_credential)
                .WaitAsync(connectCts.Token)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "MCP cache connecting. Host={Host} Port={Port} UserBound={UserBound} ConnectTimeoutMs={ConnectTimeoutMs}",
                host,
                port,
                _options.RedisUserBound,
                ConnectTimeoutMilliseconds);

            _mux = await ConnectionMultiplexer.ConnectAsync(configuration)
                .WaitAsync(connectCts.Token)
                .ConfigureAwait(false);
            _db = _mux.GetDatabase();
            _logger.LogInformation("MCP cache connected.");
            return _db;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MCP cache connect failed.");
            return null;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_mux is not null)
        {
            await _mux.CloseAsync().ConfigureAwait(false);
            _mux.Dispose();
        }

        _connectLock.Dispose();
    }

    private static TokenCredential CreateCredential()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")))
        {
            return new ManagedIdentityCredential();
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true
        });
    }
}
