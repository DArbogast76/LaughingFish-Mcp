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
        "One species-guide chapter by id or by species plus topic. For a story, tale, or fiction about a species, pass that species and topic fictional-story, then print the returned body in full with no rewrite. Not live water or weather. Do not call for a general boat, beach, or trip plan.";

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
