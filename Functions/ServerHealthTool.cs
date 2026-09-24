using System.Diagnostics;
using LaughingFish.Mcp.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// First MCP tool. Confirms the MCP extension is wired.
/// Does not call Redis, Maps, or downstream APIs.
/// </summary>
public sealed class ServerHealthTool
{
    public const string ToolName = "server_health";
    public const string ToolDescription =
        "Returns LaughingFish MCP host health and whether Redis, Maps, and API base URLs are bound. Does not fetch water temperature, weather, or sunrise/sunset.";

    private readonly ILogger<ServerHealthTool> _logger;
    private readonly McpOptions _options;

    public ServerHealthTool(ILogger<ServerHealthTool> logger, IOptions<McpOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    [Function(nameof(ServerHealthTool))]
    public object Run(
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
                    azureMapsBound = _options.AzureMapsBound,
                    waterTempApiBound = _options.WaterTempApiBound,
                    waterTempApiAudienceBound = _options.WaterTempApiAudienceBound,
                    weatherApiBound = _options.WeatherApiBound,
                    weatherApiAudienceBound = _options.WeatherApiAudienceBound,
                    sunriseSunsetApiBound = _options.SunriseSunsetApiBound,
                    sunriseSunsetApiAudienceBound = _options.SunriseSunsetApiAudienceBound,
                    pinRecentHours = _options.PinRecentHours
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
