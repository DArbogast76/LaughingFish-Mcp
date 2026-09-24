using System.Diagnostics;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Shared place-to-coordinates tool. Weather, water temp, and solar tools
/// should call ILocationResolver directly; this tool is for the model when
/// it only needs coordinates.
/// </summary>
public sealed class ResolveLocationTool
{
    public const string ToolName = "resolve_location";
    public const string ToolDescription =
        "Converts a place name, city, or address into latitude and longitude using Azure Maps. Use this before calling APIs that need coordinates, or pass the same place string to get_sunrise_sunset or get_weather_forecast. Does not invent coordinates when Maps fails.";

    private readonly ILogger<ResolveLocationTool> _logger;
    private readonly ILocationResolver _resolver;

    public ResolveLocationTool(ILogger<ResolveLocationTool> logger, ILocationResolver resolver)
    {
        _logger = logger;
        _resolver = resolver;
    }

    [Function(nameof(ResolveLocationTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Example: Anchorage, Alaska.", true)] string place,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "ResolveLocation tool started. InvocationId={InvocationId} Tool={Tool} Place={Place}",
            invocationId,
            context.Name,
            place);

        try
        {
            var location = await _resolver.ResolveAsync(place, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "ResolveLocation tool succeeded. InvocationId={InvocationId} Lat={Lat} Lon={Lon} ElapsedMs={ElapsedMs}",
                invocationId,
                location.Latitude,
                location.Longitude,
                started.ElapsedMilliseconds);

            return new
            {
                ok = true,
                invocationId,
                location = ToPayload(location)
            };
        }
        catch (LocationResolutionException ex)
        {
            _logger.LogInformation(
                "ResolveLocation tool rejected. InvocationId={InvocationId} Error={Error} ElapsedMs={ElapsedMs}",
                invocationId,
                ex.ErrorCode,
                started.ElapsedMilliseconds);
            return new
            {
                ok = false,
                error = ex.ErrorCode,
                message = ex.Message,
                invocationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ResolveLocation tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    public static object ToPayload(ResolvedLocation location) => new
    {
        latitude = location.Latitude,
        longitude = location.Longitude,
        query = location.Query,
        formattedAddress = location.FormattedAddress,
        locality = location.Locality,
        adminDistrict = location.AdminDistrict,
        countryRegion = location.CountryRegion
    };
}
