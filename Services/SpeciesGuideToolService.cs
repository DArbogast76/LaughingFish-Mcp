using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
using LaughingFish.Mcp.Functions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Services;

/// <summary>
/// Shared execution for MCP tools and HTTP tool APIs. Validates inputs, then proxies Species Guide.
/// </summary>
public sealed class SpeciesGuideToolService : ISpeciesGuideToolService
{
    private static readonly int[] AllowedTop = [1, 2, 3];

    private readonly ISpeciesGuideApiClient _client;
    private readonly McpOptions _options;
    private readonly ILogger<SpeciesGuideToolService> _logger;

    public SpeciesGuideToolService(
        ISpeciesGuideApiClient client,
        IOptions<McpOptions> options,
        ILogger<SpeciesGuideToolService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SpeciesGuideToolResult> ListSpeciesAsync(
        string invocationId,
        CancellationToken cancellationToken)
    {
        var result = await _client.ListSpeciesAsync(invocationId, cancellationToken).ConfigureAwait(false);
        return Wrap(result, "species", invocationId);
    }

    public async Task<SpeciesGuideToolResult> ListTopicsAsync(
        string invocationId,
        CancellationToken cancellationToken)
    {
        var result = await _client.ListTopicsAsync(invocationId, cancellationToken).ConfigureAwait(false);
        return Wrap(result, "topics", invocationId);
    }

    public async Task<SpeciesGuideToolResult> SearchAsync(
        string? species,
        string? query,
        string? topic,
        int? top,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(species))
        {
            return SpeciesGuideToolResult.Validation("missing_species", "species is required.", invocationId);
        }

        var resolvedTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim().ToLowerInvariant();
        if (resolvedTopic is not null && !SpeciesGuideTopics.IsPublished(resolvedTopic))
        {
            _logger.LogInformation(
                "SpeciesGuide search rejected topic. InvocationId={InvocationId} Topic={Topic}",
                invocationId,
                topic);
            return SpeciesGuideToolResult.Validation(
                "invalid_topic",
                "topic must be one of: general, science, habitat-behavior, gear-tackle, techniques, hotspots, time-temperature, reading-water, tips-tricks, conservation, boat-control, electronics, related-media, sizes-records, consumption-recipes, fictional-story.",
                invocationId);
        }

        if (!TryResolveTop(top, out var resolvedTop))
        {
            return SpeciesGuideToolResult.Validation("invalid_top", "top must be 1, 2, or 3.", invocationId);
        }

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        if (!hasQuery && resolvedTopic is null)
        {
            return SpeciesGuideToolResult.Validation(
                "missing_query_or_topic",
                "Provide query, or a published topic.",
                invocationId);
        }

        var result = await _client.SearchAsync(
                species,
                hasQuery ? query : null,
                resolvedTopic,
                resolvedTop,
                invocationId,
                cancellationToken)
            .ConfigureAwait(false);

        return Wrap(result, "search", invocationId);
    }

    public async Task<SpeciesGuideToolResult> GetChapterAsync(
        string? id,
        string? species,
        string? topic,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var hasId = !string.IsNullOrWhiteSpace(id);
        var hasPair = !string.IsNullOrWhiteSpace(species) && !string.IsNullOrWhiteSpace(topic);
        if (!hasId && !hasPair)
        {
            return SpeciesGuideToolResult.Validation(
                "missing_chapter",
                "Provide id, or species and topic.",
                invocationId);
        }

        if (!hasId && !string.IsNullOrWhiteSpace(topic))
        {
            var topicKey = topic.Trim().ToLowerInvariant();
            if (!SpeciesGuideTopics.IsPublished(topicKey))
            {
                return SpeciesGuideToolResult.Validation(
                    "invalid_topic",
                    "topic must be a published key: general, science, habitat-behavior, gear-tackle, techniques, hotspots, time-temperature, reading-water, tips-tricks, conservation, boat-control, electronics, related-media, sizes-records, consumption-recipes, fictional-story.",
                    invocationId);
            }
        }

        var result = await _client.GetChapterAsync(
                hasId ? id : null,
                hasId ? null : species,
                hasId ? null : topic,
                invocationId,
                cancellationToken)
            .ConfigureAwait(false);

        return Wrap(result, "chapter", invocationId);
    }

    private SpeciesGuideToolResult Wrap(SpeciesGuideApiResult result, string operation, string invocationId)
    {
        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "SpeciesGuide downstream unsuccessful. InvocationId={InvocationId} Operation={Operation} Error={Error} StatusCode={StatusCode} ApiBound={ApiBound} AudienceBound={AudienceBound}",
                invocationId,
                operation,
                result.ErrorCode,
                result.StatusCode,
                _options.SpeciesGuideApiBound,
                _options.SpeciesGuideApiAudienceBound);
        }

        return SpeciesGuideToolResult.FromApi(result, invocationId);
    }

    private static bool TryResolveTop(int? top, out int resolved)
    {
        if (top is null)
        {
            resolved = 3;
            return true;
        }

        resolved = top.Value;
        return AllowedTop.Contains(resolved);
    }
}
