using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Current EPA UV Index for a US place or ZIP. Place uses the shared location
/// resolver and sends only the postal code. This tool does not call Azure Maps.
/// </summary>
public sealed class GetUvIndexTool
{
    public const string ToolName = "get_uv_index";
    public const string ToolDescription =
        "Current EPA UV Index forecast for a US place or five-digit ZIP. Hourly values and the daily index are the issuance EPA is publishing now. No date can be requested and no later days are available. Pass place (preferred) or zip. A place is resolved to a US ZIP before the request. Latitude and longitude are not accepted. Does not invent an index when the place has no ZIP or EPA has no forecast.";

    private readonly ILogger<GetUvIndexTool> _logger;
    private readonly IUvApiClient _client;
    private readonly ILocationResolver _locations;
    private readonly McpOptions _options;

    public GetUvIndexTool(
        ILogger<GetUvIndexTool> logger,
        IUvApiClient client,
        ILocationResolver locations,
        IOptions<McpOptions> options)
    {
        _logger = logger;
        _client = client;
        _locations = locations;
        _options = options.Value;
    }

    [Function(nameof(GetUvIndexTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("place", "Place name, city, or address. Example: Annapolis, Maryland. Preferred when the user did not give a ZIP.", false)] string? place,
        [McpToolProperty("zip", "Five-digit US ZIP. Used as-is and does not geocode. ZIP+4 keeps the first five digits.", false)] string? zip,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetUvIndex tool started. InvocationId={InvocationId} Tool={Tool} Place={Place} Zip={Zip}",
            invocationId,
            context.Name,
            place,
            zip);

        try
        {
            var result = await ExecuteAsync(place, zip, invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "GetUvIndex tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetUvIndex tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal async Task<JsonObject> ExecuteAsync(
        string? place,
        string? zip,
        string invocationId,
        CancellationToken cancellationToken)
    {
        JsonNode? location = null;
        string? resolvedZip = null;
        if (!string.IsNullOrWhiteSpace(zip))
        {
            resolvedZip = NormalizeZip(zip);
            if (resolvedZip is null)
            {
                _logger.LogInformation(
                    "GetUvIndex rejected ZIP. InvocationId={InvocationId} Zip={Zip}",
                    invocationId,
                    zip);
                return Error("invalid_zip", "zip must be a five-digit US ZIP.", invocationId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(place))
        {
            try
            {
                var resolved = await _locations.ResolveAsync(place, invocationId, cancellationToken)
                    .ConfigureAwait(false);
                location = JsonSerializer.SerializeToNode(new
                {
                    query = resolved.Query,
                    formattedAddress = resolved.FormattedAddress,
                    locality = resolved.Locality,
                    adminDistrict = resolved.AdminDistrict,
                    countryRegion = resolved.CountryRegion,
                    postalCode = resolved.PostalCode
                });
                resolvedZip = NormalizeZip(resolved.PostalCode);
                if (resolvedZip is null)
                {
                    _logger.LogInformation(
                        "GetUvIndex place has no ZIP. InvocationId={InvocationId} Place={Place} Lat={Lat} Lon={Lon}",
                        invocationId,
                        place,
                        resolved.Latitude,
                        resolved.Longitude);
                    return Error("no_postal_code", "No US ZIP was returned for that place.", invocationId, location);
                }
            }
            catch (LocationResolutionException ex)
            {
                _logger.LogInformation(
                    "GetUvIndex place rejected. InvocationId={InvocationId} Error={Error} Place={Place}",
                    invocationId,
                    ex.ErrorCode,
                    place);
                return Error(ex.ErrorCode, ex.Message, invocationId);
            }
        }
        else
        {
            return Error("missing_location", "Provide place or zip.", invocationId);
        }

        if (!_options.UvApiBound)
        {
            _logger.LogWarning("GetUvIndex skipped. InvocationId={InvocationId} Reason=uv_unbound", invocationId);
            return Error("uv_unbound", "UvApiBaseUrl is not configured.", invocationId, location);
        }

        var api = await _client.GetAsync(resolvedZip, invocationId, cancellationToken).ConfigureAwait(false);
        if (!api.IsSuccess || string.IsNullOrWhiteSpace(api.Body))
        {
            _logger.LogInformation(
                "GetUvIndex API rejected. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode} Zip={Zip}",
                invocationId,
                api.ErrorCode,
                api.StatusCode,
                resolvedZip);
            return Error(api.ErrorCode ?? "uv_unavailable", api.ErrorMessage ?? "UV API failed.", invocationId, location);
        }

        JsonNode? payload;
        try
        {
            payload = JsonNode.Parse(api.Body);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GetUvIndex body is not JSON. InvocationId={InvocationId} Zip={Zip}", invocationId, resolvedZip);
            return Error("uv_invalid_body", "UV API returned a body that is not JSON.", invocationId, location);
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["zip"] = resolvedZip,
            ["location"] = location,
            ["uv"] = payload
        };
    }

    internal static string? NormalizeZip(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 5 || !trimmed.Take(5).All(char.IsDigit))
        {
            return null;
        }

        if (trimmed.Length == 5 || (trimmed.Length > 5 && trimmed[5] == '-'))
        {
            return trimmed[..5];
        }

        return null;
    }

    private static JsonObject Error(string code, string message, string invocationId, JsonNode? location = null)
    {
        var error = new JsonObject
        {
            ["ok"] = false,
            ["error"] = code,
            ["message"] = message,
            ["invocationId"] = invocationId
        };
        if (location is not null)
        {
            error["location"] = location;
        }

        return error;
    }
}
