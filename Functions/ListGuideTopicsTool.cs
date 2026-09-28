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
        "Lists the 16 published Target Species topic keys and whether each is advice or fiction. Internal catalog only. Callers never know or choose topic keys. Do not invent keys. Does not return chapter text, live water, weather, or official regulations.";

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
