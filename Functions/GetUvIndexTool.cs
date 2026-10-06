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
/// Current EPA UV Index for a US ZIP. Latitude and longitude are required
/// and are not sent to EPA. This tool does not call Azure Maps.
/// </summary>
public sealed class GetUvIndexTool
{
    public const string ToolName = "get_uv_index";
    public const string ToolDescription =
        "Current EPA UV Index forecast for a five-digit US ZIP. Hourly values and the daily index are the issuance EPA is publishing now. No date can be requested and no later days are available. Latitude, longitude, and zip are required. zip is used as given. Does not invent an index when EPA has no forecast.";

    private readonly ILogger<GetUvIndexTool> _logger;
    private readonly IUvApiClient _client;
    private readonly McpOptions _options;

    public GetUvIndexTool(
        ILogger<GetUvIndexTool> logger,
        IUvApiClient client,
        IOptions<McpOptions> options)
    {
        _logger = logger;
        _client = client;
        _options = options.Value;
    }

    [Function(nameof(GetUvIndexTool))]
    public async Task<JsonObject> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        [McpToolProperty("zip", "Five-digit US ZIP. Used as-is. ZIP+4 keeps the first five digits.", true)] string? zip,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetUvIndex tool started. InvocationId={InvocationId} Tool={Tool} Lat={Lat} Lon={Lon} Zip={Zip}",
            invocationId,
            context.Name,
            latitude,
            longitude,
            zip);

        try
        {
            var result = await ExecuteAsync(latitude, longitude, zip, invocationId, functionContext.CancellationToken)
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
        double? latitude,
        double? longitude,
        string? zip,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!CoordinateInput.TryRead(latitude, longitude, out var lat, out var lon, out var locationError, out var locationMessage))
        {
            return Error(locationError, locationMessage, invocationId);
        }

        var resolvedZip = NormalizeZip(zip);
        if (resolvedZip is null)
        {
            _logger.LogInformation(
                "GetUvIndex rejected ZIP. InvocationId={InvocationId} Zip={Zip}",
                invocationId,
                zip);
            return Error("invalid_zip", "zip must be a five-digit US ZIP.", invocationId);
        }

        JsonNode location = new JsonObject
        {
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["postalCode"] = resolvedZip
        };

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
