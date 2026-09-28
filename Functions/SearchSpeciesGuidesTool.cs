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
        "Recreational how-to from one Target Species book. Required species is a published common name or slug. Map vernacular names first (Bull Reds → redfish). Put the user's own language in query. Callers never know topic keys; do not ask them for a topic. Optional topic is inferred and must be a published key: general, science, habitat-behavior, gear-tackle, techniques, hotspots, time-temperature, reading-water, tips-tricks, conservation, boat-control, electronics, related-media, sizes-records, consumption-recipes, fictional-story. Prefer query alone when the topic is uncertain. Optional top 1, 2, or 3 (default 3). One species per call. Fiction only when topic is fictional-story. Returns full chapter body. Not live water or weather. Not official regulations, seasons, or bag limits. Hotspot text is not a live report. Tackle lists are examples. Time and temperature text is association only, not a current reading. Do not invent species or topic keys. Do not present topic keys, slugs, or chapter ids to the caller.";

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
