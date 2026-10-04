using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for get_uv_index. Same payload as the MCP tool.
/// </summary>
public sealed class UvIndexHttp
{
    public const string Route = "v1/tools/get-uv-index";

    private readonly ILogger<UvIndexHttp> _logger;
    private readonly GetUvIndexTool _tool;

    public UvIndexHttp(ILogger<UvIndexHttp> logger, GetUvIndexTool tool)
    {
        _logger = logger;
        _tool = tool;
    }

    [Function("GetUvIndexHttp")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = Route)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var place = ToolHttpRequest.ReadString(values, "place");
            var zip = ToolHttpRequest.ReadString(values, "zip");
            _logger.LogInformation(
                "GetUvIndex HTTP started. InvocationId={InvocationId} Method={Method} Place={Place} Zip={Zip}",
                invocationId,
                req.Method,
                place,
                zip);

            var result = await _tool.ExecuteAsync(place, zip, invocationId, context.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetUvIndex HTTP finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return new ObjectResult(result) { StatusCode = StatusCodes.Status200OK };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetUvIndex HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                error = "uv_unavailable",
                invocationId
            })
            {
                StatusCode = StatusCodes.Status502BadGateway
            };
        }
    }
}
