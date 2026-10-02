using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class WaterTempApiClient : IWaterTempApiClient
{
    public const string Path = "/api/v1/water-temperature";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<WaterTempApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public WaterTempApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<WaterTempApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.3"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<WaterTempApiResult> GetAsync(
        double latitude,
        double longitude,
        int nearest,
        int days,
        int maxDistanceMiles,
        bool includeChart,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WaterTempApiBaseUrl))
        {
            _logger.LogWarning(
                "WaterTemp client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new WaterTempApiResult(
                0,
                null,
                false,
                "water_temp_unbound",
                "WaterTempApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.WaterTemperature(latitude, longitude, nearest, days, maxDistanceMiles, includeChart);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "WaterTemp client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            var cachedBody = cached.Value;
            byte[]? cachedChart = null;
            if (includeChart)
            {
                cachedChart = await TryLoadNearestChartAsync(cachedBody, days, invocationId, cancellationToken).ConfigureAwait(false);
                cachedBody = MarkChart(cachedBody, cachedChart is { Length: > 0 });
            }

            return new WaterTempApiResult(200, cachedBody, true, null, null, cachedChart);
        }

        var baseUrl = _options.WaterTempApiBaseUrl.TrimEnd('/');
        var query = $"latitude={Uri.EscapeDataString(latitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&longitude={Uri.EscapeDataString(longitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&nearest={nearest}"
            + $"&days={days}"
            + $"&maxDistanceMiles={maxDistanceMiles}"
            + (includeChart ? "&includeChart=true" : string.Empty);
        var url = $"{baseUrl}{Path}?{query}";

        string? accessToken = null;
        if (_options.WaterTempApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.WaterTempApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "WaterTemp client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "WaterTemp client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new WaterTempApiResult(
                    0,
                    null,
                    false,
                    "water_temp_token_failed",
                    "Could not acquire a token for the Water Temperature API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "WaterTemp client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "WaterTemp client request. InvocationId={InvocationId} Path={Path} Lat={Lat} Lon={Lon} Nearest={Nearest} Days={Days} MaxDistanceMiles={MaxDistanceMiles} IncludeChart={IncludeChart} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            latitude,
            longitude,
            nearest,
            days,
            maxDistanceMiles,
            includeChart,
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "WaterTemp client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode)
            {
                return new WaterTempApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "water_temp_http_error",
                    $"Water Temperature API returned {(int)response.StatusCode}.");
            }

            var shaped = Shape(body, days, includeChart, invocationId);
            byte[]? chart = null;
            if (includeChart)
            {
                chart = await TryLoadNearestChartAsync(shaped, days, invocationId, cancellationToken).ConfigureAwait(false);
                shaped = MarkChart(shaped, chart is { Length: > 0 });
            }

            await _cache.SetAsync(cacheKey, shaped, _options.WaterTempCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new WaterTempApiResult((int)response.StatusCode, shaped, true, null, null, chart);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "WaterTemp client timeout. InvocationId={InvocationId}", invocationId);
            return new WaterTempApiResult(0, null, false, "water_temp_timeout", "Water Temperature API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "WaterTemp client transport failure. InvocationId={InvocationId}", invocationId);
            return new WaterTempApiResult(
                0,
                null,
                false,
                "water_temp_unreachable",
                "Water Temperature API could not be reached.");
        }
    }

    private string Shape(string body, int days, bool includeChart, string invocationId)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var status = ReadString(root, "status") ?? "ok";
            var stationCount = ReadInt(root, "stationCount") ?? 0;
            var requestNode = ReadObject(root, "request");
            var stationsNode = ReadArray(root, "stations");

            var stations = new JsonArray();
            if (stationsNode is { } stationList)
            {
                foreach (var station in stationList.EnumerateArray())
                {
                    stations.Add(ShapeStation(station, days));
                }
            }

            var explanation = ReadObject(root, "explanation") ?? ReadObject(root, "Explanation");
            var shaped = new JsonObject
            {
                ["schema"] = ReadString(root, "schema") ?? ReadString(root, "Schema") ?? "laughingfish.waterTemperature.v1",
                ["explanation"] = explanation.HasValue
                    ? JsonNode.Parse(explanation.Value.GetRawText())
                    : null,
                ["status"] = status,
                ["stationCount"] = stationCount,
                ["request"] = requestNode.HasValue
                    ? JsonNode.Parse(requestNode.Value.GetRawText())
                    : null,
                ["stations"] = stations
            };
            if (includeChart)
            {
                shaped["chart"] = new JsonObject
                {
                    ["requested"] = true,
                    ["available"] = false,
                    ["scope"] = "nearest"
                };
            }

            RewriteChartExplanation(shaped);

            _logger.LogInformation(
                "WaterTemp client shaped response. InvocationId={InvocationId} Status={Status} StationCount={StationCount}",
                invocationId,
                status,
                stationCount);

            return shaped.ToJsonString();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "WaterTemp client shape failed. InvocationId={InvocationId}", invocationId);
            return body;
        }
    }


    private async Task<byte[]?> TryLoadNearestChartAsync(string body, int days, string invocationId, CancellationToken cancellationToken)
    {
        if (!TryReadNearestStationId(body, out var stationId))
        {
            _logger.LogInformation(
                "WaterTemp chart skipped. InvocationId={InvocationId} Reason=no_station Days={Days}",
                invocationId,
                days);
            return null;
        }

        var baseUrl = _options.WaterTempApiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/v1/water-temperature/stations/{Uri.EscapeDataString(stationId)}/charts/{days}";
        string? accessToken = null;
        if (_options.WaterTempApiAudienceBound)
        {
            try
            {
                var audience = _options.WaterTempApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : audience + "/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken).ConfigureAwait(false);
                accessToken = token.Token;
            }
            catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException)
            {
                _logger.LogWarning(ex, "WaterTemp chart token failure. InvocationId={InvocationId} StationId={StationId}", invocationId, stationId);
                return null;
            }
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "WaterTemp chart unavailable. InvocationId={InvocationId} StationId={StationId} Days={Days} StatusCode={StatusCode}",
                    invocationId,
                    stationId,
                    days,
                    (int)response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length is 0 or > 2_000_000 || !IsPng(bytes))
            {
                _logger.LogWarning(
                    "WaterTemp chart rejected. InvocationId={InvocationId} StationId={StationId} Bytes={Bytes}",
                    invocationId,
                    stationId,
                    bytes.Length);
                return null;
            }

            _logger.LogInformation(
                "WaterTemp chart loaded. InvocationId={InvocationId} StationId={StationId} Days={Days} Bytes={Bytes}",
                invocationId,
                stationId,
                days,
                bytes.Length);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "WaterTemp chart request failed. InvocationId={InvocationId} StationId={StationId} Days={Days}", invocationId, stationId, days);
            return null;
        }
    }

    private static string MarkChart(string body, bool available)
    {
        try
        {
            var node = JsonNode.Parse(body) as JsonObject;
            if (node is null)
            {
                return body;
            }

            node["chart"] = new JsonObject
            {
                ["requested"] = true,
                ["available"] = available,
                ["scope"] = "nearest"
            };
            RewriteChartExplanation(node);
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void RewriteChartExplanation(JsonObject shaped)
    {
        if (shaped["explanation"] is not JsonObject explanation)
        {
            return;
        }

        explanation["chart"] = "The chart image is the PNG for the nearest station and the same days window. It is returned only when includeChart is true. No chart image is returned unless that argument is true. An unavailable chart does not remove the temperature readings.";
    }

    private static bool TryReadNearestStationId(string body, out string stationId)
    {
        stationId = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("stations", out var stations) || stations.ValueKind != JsonValueKind.Array || stations.GetArrayLength() == 0)
            {
                return false;
            }

            var first = stations[0];
            if (!first.TryGetProperty("stationId", out var id) || id.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            stationId = id.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(stationId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= 8
        && bytes[0] == 0x89
        && bytes[1] == 0x50
        && bytes[2] == 0x4E
        && bytes[3] == 0x47;

    private static JsonObject ShapeStation(JsonElement station, int days)
    {
        var hours = FlattenHours(station);
        var currentF = ReadDouble(station, "latestTempF");
        var currentC = ReadDouble(station, "latestTempC");
        var observed = ReadString(station, "lastObservedUtc");

        double? startF = null;
        double? endF = null;
        double? startC = null;
        double? endC = null;
        string? startUtc = null;
        string? endUtc = null;
        foreach (var hour in hours)
        {
            var f = ReadDouble(hour, "tempF");
            var c = ReadDouble(hour, "tempC");
            var t = ReadString(hour, "observedUtc");
            if (f is null && c is null)
            {
                continue;
            }

            startF ??= f;
            startC ??= c;
            startUtc ??= t;
            endF = f;
            endC = c;
            endUtc = t;
        }

        string? direction = null;
        double? deltaF = null;
        double? deltaC = null;
        if (startF is { } s && endF is { } e)
        {
            deltaF = Math.Round(e - s, 1);
            direction = Math.Abs(deltaF.Value) < 0.5 ? "steady" : deltaF > 0 ? "warming" : "cooling";
        }

        if (startC is { } sc && endC is { } ec)
        {
            deltaC = Math.Round(ec - sc, 1);
        }

        var shaped = new JsonObject
        {
            ["distanceMiles"] = ReadDouble(station, "distanceMiles"),
            ["latitude"] = ReadDouble(station, "latitude"),
            ["longitude"] = ReadDouble(station, "longitude"),
            ["state"] = ReadString(station, "state"),
            ["stationId"] = ReadString(station, "stationId"),
            ["name"] = ReadString(station, "name"),
            ["observationCount"] = ReadInt(station, "observationCount"),
            ["coveragePercent"] = ReadDouble(station, "coveragePercent"),
            ["current"] = new JsonObject
            {
                ["tempF"] = currentF,
                ["tempC"] = currentC,
                ["observedUtc"] = observed
            },
            ["trend"] = new JsonObject
            {
                ["windowDays"] = days,
                ["direction"] = direction,
                ["startTempF"] = startF,
                ["endTempF"] = endF,
                ["deltaF"] = deltaF,
                ["startTempC"] = startC,
                ["endTempC"] = endC,
                ["deltaC"] = deltaC,
                ["startObservedUtc"] = startUtc,
                ["endObservedUtc"] = endUtc
            },
            ["history"] = ReadObject(station, "days") is { } history
                ? JsonNode.Parse(history.GetRawText())
                : new JsonArray()
        };

        return shaped;
    }

    private static List<JsonElement> FlattenHours(JsonElement station)
    {
        var hours = new List<JsonElement>();
        var days = ReadArray(station, "days");
        if (days is null)
        {
            return hours;
        }

        foreach (var day in days.Value.EnumerateArray())
        {
            var dayHours = ReadArray(day, "hours");
            if (dayHours is null)
            {
                continue;
            }

            foreach (var hour in dayHours.Value.EnumerateArray())
            {
                hours.Add(hour);
            }
        }

        return hours;
    }

    private static JsonElement? ReadObject(JsonElement parent, string camel)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (parent.TryGetProperty(camel, out var value) && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return value;
        }

        var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        if (parent.TryGetProperty(pascal, out value) && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return value;
        }

        return null;
    }

    private static JsonElement? ReadArray(JsonElement parent, string camel)
    {
        var value = ReadObject(parent, camel);
        return value is { ValueKind: JsonValueKind.Array } ? value : null;
    }

    private static string? ReadString(JsonElement parent, string camel)
    {
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryProperty(parent, camel, out var value)
            && value.ValueKind is JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static int? ReadInt(JsonElement parent, string camel)
    {
        if (!TryProperty(parent, camel, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;
    }

    private static double? ReadDouble(JsonElement parent, string camel)
    {
        if (!TryProperty(parent, camel, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) ? n : null;
    }

    private static bool TryProperty(JsonElement parent, string camel, out JsonElement value)
    {
        if (parent.TryGetProperty(camel, out value))
        {
            return true;
        }

        var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        return parent.TryGetProperty(pascal, out value);
    }

    private static TokenCredential CreateCredential()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")))
        {
            return new ManagedIdentityCredential();
        }

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true
        });
    }
}
