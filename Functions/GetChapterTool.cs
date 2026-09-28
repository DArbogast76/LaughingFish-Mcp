using System.Diagnostics;
using LaughingFish.Mcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Fetches one published chapter by id or by species plus topic.
/// </summary>
public sealed class GetChapterTool
{
    public const string ToolName = "get_chapter";
    public const string ToolDescription =
        "Returns one Target Species chapter by id (speciesSlug-topic) or by species plus an inferred published topic key. Full chapter body. Callers never know ids or topic keys; do not ask them for either. Not live water or weather. Not official regulations, seasons, or bag limits. Do not invent ids or topic keys. Do not present ids, slugs, or topic keys to the caller.";

    private readonly ILogger<GetChapterTool> _logger;
    private readonly ISpeciesGuideToolService _service;

    public GetChapterTool(ILogger<GetChapterTool> logger, ISpeciesGuideToolService service)
    {
        _logger = logger;
        _service = service;
    }

    [Function(nameof(GetChapterTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("id", "Chapter id. Example: haddock-time-temperature.", false)] string? id,
        [McpToolProperty("species", "Published common name or slug when id is not provided.", false)] string? species,
        [McpToolProperty("topic", "Published topic key when id is not provided.", false)] string? topic,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "GetChapter tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Id={Id} Species={Species} Topic={Topic}",
            invocationId,
            context.Name,
            context.SessionId,
            id,
            species,
            topic);

        try
        {
            var result = await _service.GetChapterAsync(
                    id,
                    species,
                    topic,
                    invocationId,
                    functionContext.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "GetChapter tool finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return result.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetChapter tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
