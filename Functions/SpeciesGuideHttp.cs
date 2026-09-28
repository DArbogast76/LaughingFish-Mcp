using System.Diagnostics;
using LaughingFish.Mcp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP surface for the four species-guide tools. Same payloads as the MCP tools.
/// Other apps call these routes. Models use the MCP webhook.
/// </summary>
public sealed class SpeciesGuideHttp
{
    public const string ListSpeciesRoute = "v1/tools/list-species";
    public const string ListTopicsRoute = "v1/tools/list-guide-topics";
    public const string SearchRoute = "v1/tools/search-species-guides";
    public const string ChapterRoute = "v1/tools/get-chapter";

    private readonly ILogger<SpeciesGuideHttp> _logger;
    private readonly ISpeciesGuideToolService _service;

    public SpeciesGuideHttp(ILogger<SpeciesGuideHttp> logger, ISpeciesGuideToolService service)
    {
        _logger = logger;
        _service = service;
    }

    [Function("ListSpeciesHttp")]
    public Task<IActionResult> ListSpecies(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = ListSpeciesRoute)] HttpRequest req,
        FunctionContext context) =>
        Execute(req, context, "list_species", (invocationId, token) =>
            _service.ListSpeciesAsync(invocationId, token));

    [Function("ListGuideTopicsHttp")]
    public Task<IActionResult> ListTopics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = ListTopicsRoute)] HttpRequest req,
        FunctionContext context) =>
        Execute(req, context, "list_guide_topics", (invocationId, token) =>
            _service.ListTopicsAsync(invocationId, token));

    [Function("SearchSpeciesGuidesHttp")]
    public async Task<IActionResult> Search(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = SearchRoute)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var species = ToolHttpRequest.ReadString(values, "species");
            var query = ToolHttpRequest.ReadString(values, "query");
            var topic = ToolHttpRequest.ReadString(values, "topic");
            var top = ToolHttpRequest.ReadInt(values, "top");

            _logger.LogInformation(
                "SearchSpeciesGuides HTTP started. InvocationId={InvocationId} Method={Method} Species={Species} QueryLength={QueryLength} Topic={Topic} Top={Top}",
                invocationId,
                req.Method,
                species,
                query?.Length ?? 0,
                topic,
                top);

            var result = await _service.SearchAsync(
                    species,
                    query,
                    topic,
                    top,
                    invocationId,
                    context.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "SearchSpeciesGuides HTTP finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return new ObjectResult(result.Body) { StatusCode = result.HttpStatus };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SearchSpeciesGuides HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    [Function("GetChapterHttp")]
    public async Task<IActionResult> GetChapter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = ChapterRoute)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        try
        {
            var values = await ToolHttpRequest.ReadAsync(req, context.CancellationToken).ConfigureAwait(false);
            var id = ToolHttpRequest.ReadString(values, "id");
            var species = ToolHttpRequest.ReadString(values, "species");
            var topic = ToolHttpRequest.ReadString(values, "topic");

            _logger.LogInformation(
                "GetChapter HTTP started. InvocationId={InvocationId} Method={Method} Id={Id} Species={Species} Topic={Topic}",
                invocationId,
                req.Method,
                id,
                species,
                topic);

            var result = await _service.GetChapterAsync(
                    id,
                    species,
                    topic,
                    invocationId,
                    context.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "GetChapter HTTP finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return new ObjectResult(result.Body) { StatusCode = result.HttpStatus };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetChapter HTTP failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<IActionResult> Execute(
        HttpRequest req,
        FunctionContext context,
        string tool,
        Func<string, CancellationToken, Task<SpeciesGuideToolResult>> run)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        _logger.LogInformation(
            "SpeciesGuide HTTP started. InvocationId={InvocationId} Tool={Tool} Method={Method} Path={Path}",
            invocationId,
            tool,
            req.Method,
            req.Path.Value);

        try
        {
            var result = await run(invocationId, context.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "SpeciesGuide HTTP finished. InvocationId={InvocationId} Tool={Tool} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                tool,
                result.HttpStatus,
                started.ElapsedMilliseconds);
            return new ObjectResult(result.Body) { StatusCode = result.HttpStatus };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SpeciesGuide HTTP failed. InvocationId={InvocationId} Tool={Tool} ElapsedMs={ElapsedMs}",
                invocationId,
                tool,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
