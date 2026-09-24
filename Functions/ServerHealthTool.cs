using System.Diagnostics;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Confirms the MCP host is wired and whether Redis answers a ping.
/// Does not fetch water temperature, weather, or sunrise/sunset.
/// </summary>
public sealed class ServerHealthTool
{
    public const string ToolName = "server_health";
    public const string ToolDescription =
        "Returns LaughingFish MCP host health, Redis connectivity, and whether Maps and API base URLs are bound. Does not fetch water temperature, weather, or sunrise/sunset.";

    private readonly ILogger<ServerHealthTool> _logger;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;

    public ServerHealthTool(ILogger<ServerHealthTool> logger, IOptions<McpOptions> options, IMcpCache cache)
    {
        _logger = logger;
        _options = options.Value;
        _cache = cache;
    }

    [Function(nameof(ServerHealthTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "ServerHealth tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId}",
            invocationId,
            context.Name,
            context.SessionId);

        try
        {
            var redisConnected = await _cache.PingAsync(invocationId, functionContext.CancellationToken).ConfigureAwait(false);
            var payload = new
            {
                status = "ok",
                utc = DateTime.UtcNow.ToString("o"),
                invocationId,
                tool = ToolName,
                worker = "dotnet-isolated",
                targetFramework = "net10.0",
                app = "LaughingFish.Mcp",
                settings = new
                {
                    redisHostBound = _options.RedisHostBound,
                    redisPort = _options.RedisPort,
                    redisUserBound = _options.RedisUserBound,
                    redisConnected,
                    azureMapsBound = _options.AzureMapsBound,
                    waterTempApiBound = _options.WaterTempApiBound,
                    waterTempApiAudienceBound = _options.WaterTempApiAudienceBound,
                    weatherApiBound = _options.WeatherApiBound,
                    weatherApiAudienceBound = _options.WeatherApiAudienceBound,
                    sunriseSunsetApiBound = _options.SunriseSunsetApiBound,
                    sunriseSunsetApiAudienceBound = _options.SunriseSunsetApiAudienceBound,
                    pinRecentHours = _options.PinRecentHours,
                    weatherCacheTtlSeconds = (int)_options.WeatherCacheTtl.TotalSeconds,
                    waterTempCacheTtlSeconds = (int)_options.WaterTempCacheTtl.TotalSeconds,
                    sunriseCacheTtlSeconds = (int)_options.SunriseCacheTtl.TotalSeconds,
                    mapsCacheTtlSeconds = (int)_options.MapsCacheTtl.TotalSeconds,
                    cacheDefaultTtlSeconds = (int)_options.DefaultCacheTtl.TotalSeconds
                }
            };

            _logger.LogInformation(
                "ServerHealth tool succeeded. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);

            return payload;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ServerHealth tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
