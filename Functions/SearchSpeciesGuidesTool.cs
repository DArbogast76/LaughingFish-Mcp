using System.Diagnostics;
using LaughingFish.Mcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// How-to search against one Target Species book. Fiction only via topic fictional-story.
/// </summary>
public sealed class SearchSpeciesGuidesTool
{
    public const string ToolName = "search_species_guides";
    public const string ToolDescription =
        "How-to and species facts from one published species book. Call this when the user names a fish or asks about that fish, including tell me about, what is, or how to catch. Required species (published name or slug; map nicknames first). Put the user's language in query. Optional published topic key and top 1-3 (default 3). Write from every returned chapter body: structure, timing, bait, presentation, rig, and tackle as stated. Keep those specifics; do not shrink the answer to a short overview. Do not use this tool for a story, tale, or fiction; use get_chapter with topic fictional-story instead. Not live water or weather. Not regulations. Do not use for a boat, beach, or trip plan that does not mention a fish.";

    private readonly ILogger<SearchSpeciesGuidesTool> _logger;
    private readonly ISpeciesGuideToolService _service;

    public SearchSpeciesGuidesTool(ILogger<SearchSpeciesGuidesTool> logger, ISpeciesGuideToolService service)
    {
        _logger = logger;
        _service = service;
    }

    [Function(nameof(SearchSpeciesGuidesTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("species", "Published common name or slug. Example: redfish. Map nicknames first. One species per call.", true)] string species,
        [McpToolProperty("query", "User language about that species. Example: where they hold in current. Prefer this over guessing a topic.", false)] string? query,
        [McpToolProperty("topic", "Optional published topic key. Use fictional-story only for the story chapter.", false)] string? topic,
        [McpToolProperty("top", "How many chapters to return. Allowed: 1, 2, 3. Default 3.", false)] int? top,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "SearchSpeciesGuides tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Species={Species} QueryLength={QueryLength} Topic={Topic} Top={Top}",
            invocationId,
            context.Name,
            context.SessionId,
            species,
            query?.Length ?? 0,
            topic,
            top);

        try
        {
            var result = await _service.SearchAsync(
                    species,
                    query,
                    topic,
                    top,
                    invocationId,
                    functionContext.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "SearchSpeciesGuides tool finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return result.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SearchSpeciesGuides tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
