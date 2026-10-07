using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Read-only catalog of HTTP tool routes. Does not call Redis, Maps, or a downstream API.
/// GET /v1/tools
/// </summary>
public sealed class ToolsCatalogHttp
{
    public const string Route = "v1/tools";

    private readonly ILogger<ToolsCatalogHttp> _logger;

    public ToolsCatalogHttp(ILogger<ToolsCatalogHttp> logger)
    {
        _logger = logger;
    }

    [Function("ToolsCatalog")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Route)] HttpRequest req,
        FunctionContext context)
    {
        var started = Stopwatch.StartNew();
        var invocationId = context.InvocationId;
        _logger.LogInformation(
            "ToolsCatalog started. InvocationId={InvocationId} Method={Method} Path={Path}",
            invocationId,
            req.Method,
            req.Path.Value);

        try
        {
            var tools = ToolCatalog.All.Select(ToPayload).ToArray();
            var payload = new
            {
                ok = true,
                schema = ToolCatalog.Schema,
                invocationId,
                catalogVersion = "5",
                path = ToolCatalog.Path,
                toolCount = tools.Length,
                calling = new
                {
                    methods = new[] { "GET", "POST" },
                    auth = "anonymous",
                    query = "Query parameters are strings. A POST JSON object wins when the same key is on the query string. Invalid JSON is ignored and the query stands.",
                    conditionTools = "get_sunrise_sunset, get_weather_forecast, get_water_temperature, get_lunar_cycle, get_tide_predictions, get_tides, get_sea_conditions, get_uv_index, get_weather_alerts, get_swim_risk, and get_charted_hazards return HTTP 200. A tool failure is HTTP 200 with ok false, error, and message. It is not an empty success.",
                    speciesGuideTools = "list_species, list_guide_topics, search_species_guides, and get_chapter return the tool result status. Success is 200. A bad request is 400. A missing chapter or species is 404. A downstream failure is 502.",
                    hostFailure = "A thrown host failure is HTTP 500 with ok false."
                },
                tools,
                notOnHttp = ToolCatalog.NotOnHttp.Select(gap => new { name = gap.Name, reason = gap.Reason }).ToArray()
            };

            _logger.LogInformation(
                "ToolsCatalog succeeded. InvocationId={InvocationId} ToolCount={ToolCount} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                tools.Length,
                StatusCodes.Status200OK,
                started.ElapsedMilliseconds);
            return new OkObjectResult(payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ToolsCatalog failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            return new ObjectResult(new
            {
                ok = false,
                schema = ToolCatalog.Schema,
                invocationId,
                status = "catalog_failed",
                message = "The tool catalog could not be read."
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static object ToPayload(ToolCatalogEntry entry) => new
    {
        name = entry.Name,
        methods = new[] { "GET", "POST" },
        path = entry.Path,
        instruction = entry.Instruction,
        inputs = entry.Inputs.Select(input => new
        {
            name = input.Name,
            required = input.Required,
            type = input.Type,
            rule = input.Rule,
            aliases = input.Aliases
        }),
        success = entry.Success,
        emptySuccess = entry.EmptySuccess,
        errors = entry.Errors,
        example = entry.Example
    };
}
