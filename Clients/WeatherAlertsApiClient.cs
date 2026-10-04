using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class WeatherAlertsApiClient : IWeatherAlertsApiClient
{
    public const string Path = "/api/v1/weather-alerts";
    public const string Schema = "laughingfish.weather.alerts.v1";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<WeatherAlertsApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public WeatherAlertsApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<WeatherAlertsApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(90);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.4"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<WeatherAlertsApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!_options.WeatherAlertsApiBound)
        {
            _logger.LogWarning(
                "Weather alerts client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new WeatherAlertsApiResult(0, null, false, "weather_alerts_unbound", "WeatherAlertsApiBaseUrl is not configured.");
        }

        var pointLatitude = Truncate4(latitude);
        var pointLongitude = Truncate4(longitude);
        var cacheKey = McpCacheKeys.WeatherAlerts(pointLatitude, pointLongitude);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            var fresh = DropExpired(cached.Value, invocationId, cacheKey);
            if (fresh is not null)
            {
                _logger.LogInformation(
                    "Weather alerts client cache hit. InvocationId={InvocationId} Key={Key} Point={Point}",
                    invocationId,
                    cacheKey,
                    Point(pointLatitude, pointLongitude));
                return new WeatherAlertsApiResult(200, fresh, true, null, null);
            }
        }

        var url = $"{_options.WeatherAlertsApiBaseUrl.TrimEnd('/')}{Path}";
        string? accessToken = null;
        if (_options.WeatherAlertsApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.WeatherAlertsApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "Weather alerts client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Weather alerts client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new WeatherAlertsApiResult(0, null, false, "weather_alerts_token_failed", "Could not acquire a token for the Weather Alerts API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "Weather alerts client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "Weather alerts client request. InvocationId={InvocationId} Path={Path} Point={Point} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            Point(pointLatitude, pointLongitude),
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                $"{{\"latitude\":{pointLatitude.ToString(CultureInfo.InvariantCulture)},\"longitude\":{pointLongitude.ToString(CultureInfo.InvariantCulture)}}}",
                Encoding.UTF8,
                "application/json");
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Weather alerts client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength} Point={Point}",
                invocationId,
                (int)response.StatusCode,
                body.Length,
                Point(pointLatitude, pointLongitude));

            if (!response.IsSuccessStatusCode || !IsAlertsPayload(body))
            {
                return new WeatherAlertsApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "weather_alerts_http_error",
                    $"Weather Alerts API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.WeatherAlertsCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new WeatherAlertsApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Weather alerts client timeout. InvocationId={InvocationId} Point={Point}",
                invocationId,
                Point(pointLatitude, pointLongitude));
            return new WeatherAlertsApiResult(0, null, false, "weather_alerts_timeout", "Weather Alerts API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Weather alerts client transport failure. InvocationId={InvocationId} Point={Point}",
                invocationId,
                Point(pointLatitude, pointLongitude));
            return new WeatherAlertsApiResult(0, null, false, "weather_alerts_unreachable", "Weather Alerts API could not be reached.");
        }
    }

    private string? DropExpired(string body, string invocationId, string cacheKey)
    {
        try
        {
            var node = JsonNode.Parse(body) as JsonObject;
            if (node is null || node["schema"]?.GetValue<string>() != Schema || node["alerts"] is not JsonArray alerts)
            {
                return null;
            }

            var now = DateTimeOffset.UtcNow;
            var kept = new JsonArray();
            var dropped = 0;
            foreach (var alert in alerts)
            {
                if (alert is not JsonObject item)
                {
                    dropped++;
                    continue;
                }

                var expires = item["expires"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(expires)
                    && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt)
                    && expiresAt <= now)
                {
                    dropped++;
                    continue;
                }

                kept.Add(item.DeepClone());
            }

            if (dropped > 0)
            {
                node["alerts"] = kept;
                _logger.LogInformation(
                    "Weather alerts cache dropped expired. InvocationId={InvocationId} Key={Key} Dropped={Dropped} Kept={Kept}",
                    invocationId,
                    cacheKey,
                    dropped,
                    kept.Count);
            }

            return node.ToJsonString();
        }
        catch (JsonException ex)
        {
            _logger.LogInformation(ex, "Weather alerts cache entry ignored. InvocationId={InvocationId} Key={Key}", invocationId, cacheKey);
            return null;
        }
    }

    private static bool IsAlertsPayload(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("schema", out var schema)
                && schema.GetString() == Schema
                && document.RootElement.TryGetProperty("alerts", out var alerts)
                && alerts.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static double Truncate4(double value) => Math.Truncate(value * 10000d) / 10000d;

    private static string Point(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{latitude},{longitude}");

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
