using System.Diagnostics;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// The only tool that accepts a place name. Returns latitude, longitude,
/// and the US postal code. Domain tools do not geocode.
/// </summary>
public sealed class ResolveLocationTool
{
    public const string ToolName = "resolve_location";
    public const string ToolDescription =
        "Converts a place name, city, address, or ZIP into latitude, longitude, and the US postal code when one is returned. A repeated place is served from cache when present. Does not invent coordinates or a postal code when the lookup fails.";

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
        [McpToolProperty("place", "Place name, city, address, or ZIP. Example: Anchorage, Alaska.", true)] string place,
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
                "ResolveLocation tool succeeded. InvocationId={InvocationId} Lat={Lat} Lon={Lon} PostalCode={PostalCode} ElapsedMs={ElapsedMs}",
                invocationId,
                location.Latitude,
                location.Longitude,
                location.PostalCode,
                started.ElapsedMilliseconds);

            return new
            {
                ok = true,
                invocationId,
                location = ToResolvePayload(location)
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

    private static object ToResolvePayload(ResolvedLocation location) => new
    {
        latitude = location.Latitude,
        longitude = location.Longitude,
        query = location.Query,
        formattedAddress = location.FormattedAddress,
        locality = location.Locality,
        adminDistrict = location.AdminDistrict,
        countryRegion = location.CountryRegion,
        postalCode = location.PostalCode
    };
}
