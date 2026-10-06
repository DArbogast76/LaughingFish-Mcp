using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Location;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Proxies calculated moon phase and rise/set times. No tide or solunar score.
/// </summary>
public sealed class GetLunarCycleTool
{
    public const string ToolName = "get_lunar_cycle";
    public const string ToolDescription =
        "Moon phase, illumination, moonrise, transit, and moonset for a date. Latitude and longitude are required. Pass date, or startDate and endDate (yyyy-MM-dd, inclusive, at most 31 days). Convert relative dates before calling. Do not pass a time zone. Does not return tides or a solunar score. Do not invent moon times.";

    private readonly ILogger<GetLunarCycleTool> _logger;
    private readonly ILunarCycleApiClient _client;

    public GetLunarCycleTool(
        ILogger<GetLunarCycleTool> logger,
        ILunarCycleApiClient client
    )
    {
        _logger = logger;
        _client = client;
    }

    [Function(nameof(GetLunarCycleTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        [McpToolProperty("latitude", "Latitude in decimal degrees, from -90 to 90.", true)] double? latitude,
        [McpToolProperty("longitude", "Longitude in decimal degrees, from -180 to 180.", true)] double? longitude,
        [McpToolProperty("date", "Single calendar date yyyy-MM-dd. Defaults to today's UTC date when startDate is omitted.", false)] string? date,
        [McpToolProperty("startDate", "Range start yyyy-MM-dd. Use with endDate for a trip. At most 31 days.", false)] string? startDate,
        [McpToolProperty("endDate", "Range end yyyy-MM-dd, inclusive.", false)] string? endDate,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;
        _logger.LogInformation(
            "GetLunarCycle tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId} Lat={Lat} Lon={Lon} Date={Date} StartDate={StartDate} EndDate={EndDate}",
            invocationId,
            context.Name,
            context.SessionId,
            latitude,
            longitude,
            date,
            startDate,
            endDate);

        try
        {
            var result = await ExecuteAsync(
                latitude,
                longitude,
                date,
                startDate,
                endDate,
                invocationId,
                functionContext.CancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "GetLunarCycle tool finished. InvocationId={InvocationId} Ok={Ok} ElapsedMs={ElapsedMs}",
                invocationId,
                result is JsonObject obj && obj["ok"]?.GetValue<bool>() == true,
                started.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "GetLunarCycle tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }

    internal async Task<JsonObject> ExecuteAsync(
        double? latitude,
        double? longitude,
        string? date,
        string? startDate,
        string? endDate,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveRange(date, startDate, endDate, out var start, out var end, out var errorCode, out var errorMessage))
        {
            return Error(errorCode, errorMessage, invocationId);
        }

        if (!CoordinateInput.TryRead(latitude, longitude, out var lat, out var lon, out var locationError, out var locationMessage))
        {
            return Error(locationError, locationMessage, invocationId);
        }

        JsonNode locationPayload = new JsonObject
        {
            ["latitude"] = lat,
            ["longitude"] = lon
        };

        var result = await _client.GetAsync(lat, lon, start, end, invocationId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "GetLunarCycle downstream unsuccessful. InvocationId={InvocationId} Error={Error} StatusCode={StatusCode}",
                invocationId,
                result.ErrorCode,
                result.StatusCode);
            var failed = Error(result.ErrorCode ?? "lunar_cycle_http_error", null, invocationId);
            if (result.StatusCode > 0)
            {
                failed["statusCode"] = result.StatusCode;
            }

            return failed;
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["location"] = locationPayload,
            ["result"] = ParseNode(result.Body)
        };
    }

    internal static bool TryResolveRange(
        string? date,
        string? startDate,
        string? endDate,
        out string start,
        out string end,
        out string errorCode,
        out string? errorMessage)
    {
        start = string.Empty;
        end = string.Empty;
        errorCode = "invalid_date";
        errorMessage = "date must be yyyy-MM-dd.";

        var hasRange = !string.IsNullOrWhiteSpace(startDate) || !string.IsNullOrWhiteSpace(endDate);
        if (!hasRange)
        {
            if (!TryParseDate(date, allowEmpty: true, out var single, out errorMessage))
            {
                return false;
            }

            start = single;
            end = single;
            errorCode = string.Empty;
            return true;
        }

        if (!TryParseDate(startDate, allowEmpty: false, out var rangeStart, out errorMessage))
        {
            errorMessage ??= "startDate must be yyyy-MM-dd.";
            return false;
        }

        if (!TryParseDate(string.IsNullOrWhiteSpace(endDate) ? startDate : endDate, allowEmpty: false, out var rangeEnd, out errorMessage))
        {
            errorMessage ??= "endDate must be yyyy-MM-dd.";
            return false;
        }

        if (string.CompareOrdinal(rangeEnd, rangeStart) < 0)
        {
            errorMessage = "endDate must be on or after startDate.";
            return false;
        }

        var startDay = DateOnly.ParseExact(rangeStart, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDay = DateOnly.ParseExact(rangeEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (endDay.DayNumber - startDay.DayNumber + 1 > 31)
        {
            errorMessage = "date range must be 31 days or fewer.";
            return false;
        }

        start = rangeStart;
        end = rangeEnd;
        errorCode = string.Empty;
        errorMessage = null;
        return true;
    }

    private static bool TryParseDate(string? raw, bool allowEmpty, out string formatted, out string? error)
    {
        formatted = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!allowEmpty)
            {
                error = "date must be yyyy-MM-dd.";
                return false;
            }

            formatted = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        var trimmed = raw.Trim().Trim('"');
        if (!TryReadDate(trimmed, out var parsed))
        {
            error = "date must be yyyy-MM-dd.";
            return false;
        }

        if (parsed.Year is < 1900 or > 2100)
        {
            error = "date year must be between 1900 and 2100.";
            return false;
        }

        formatted = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryReadDate(string trimmed, out DateOnly parsed)
    {
        if (trimmed.Length >= 10
            && trimmed[4] == '-'
            && DateOnly.TryParseExact(trimmed[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                trimmed,
                ["yyyy-MM-dd", "yyyy-M-d", "M/d/yyyy", "MM/dd/yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            parsed = DateOnly.FromDateTime(dto.UtcDateTime);
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            parsed = DateOnly.FromDateTime(dt.ToUniversalTime());
            return true;
        }

        parsed = default;
        return false;
    }

    private static JsonObject Error(string? error, string? message, string invocationId)
    {
        var body = new JsonObject
        {
            ["ok"] = false,
            ["error"] = error,
            ["invocationId"] = invocationId
        };
        if (!string.IsNullOrWhiteSpace(message))
        {
            body["message"] = message;
        }

        return body;
    }

    private static JsonNode ParseNode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(body) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return JsonValue.Create(body)!;
        }
    }
}
