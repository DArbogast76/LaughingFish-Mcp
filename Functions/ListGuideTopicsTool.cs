using System.Diagnostics;
using LaughingFish.Mcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Lists the 16 published topic keys. No Search call.
/// </summary>
public sealed class ListGuideTopicsTool
{
    public const string ToolName = "list_guide_topics";
    public const string ToolDescription =
        "Lists published species-guide topic keys. Internal only. Use only when choosing a topic for a species-guide call. Do not call for a general boat, beach, or trip plan.";

    private readonly ILogger<ListGuideTopicsTool> _logger;
    private readonly ISpeciesGuideToolService _service;

    public ListGuideTopicsTool(ILogger<ListGuideTopicsTool> logger, ISpeciesGuideToolService service)
    {
        _logger = logger;
        _service = service;
    }

    [Function(nameof(ListGuideTopicsTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "ListGuideTopics tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId}",
            invocationId,
            context.Name,
            context.SessionId);

        try
        {
            var result = await _service.ListTopicsAsync(invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "ListGuideTopics tool finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return result.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ListGuideTopics tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
