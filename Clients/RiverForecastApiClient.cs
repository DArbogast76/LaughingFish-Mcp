using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class RiverForecastApiClient : IRiverForecastApiClient
{
    public const string Path = "/api/v1/river-forecast";
    public const string Schema = "laughingfish.riverForecast.v1";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<RiverForecastApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public RiverForecastApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<RiverForecastApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(90);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.4"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<RiverForecastApiResult> GetAsync(
        double latitude,
        double longitude,
        int radiusMiles,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!_options.RiverForecastApiBound)
        {
            _logger.LogWarning(
                "River forecast client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new RiverForecastApiResult(0, null, false, "river_forecast_unbound", "RiverForecastApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.RiverForecast(latitude, longitude, radiusMiles);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit)
        {
            _logger.LogInformation(
                "River forecast client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new RiverForecastApiResult(200, cached.Value, true, null, null);
        }

        var url = $"{_options.RiverForecastApiBaseUrl.TrimEnd('/')}{Path}";
        string? accessToken = null;
        if (_options.RiverForecastApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.RiverForecastApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "River forecast client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "River forecast client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new RiverForecastApiResult(0, null, false, "river_forecast_token_failed", "Could not acquire a token for the River Forecast API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "River forecast client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "River forecast client request. InvocationId={InvocationId} Path={Path} Latitude={Latitude} Longitude={Longitude} RadiusMiles={RadiusMiles} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            latitude,
            longitude,
            radiusMiles,
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                $"{{\"latitude\":{latitude.ToString(CultureInfo.InvariantCulture)},\"longitude\":{longitude.ToString(CultureInfo.InvariantCulture)},\"radiusMiles\":{radiusMiles.ToString(CultureInfo.InvariantCulture)}}}",
                Encoding.UTF8,
                "application/json");
            if (accessToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            var started = Stopwatch.StartNew();
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "River forecast client response. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                (int)response.StatusCode,
                started.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new RiverForecastApiResult((int)response.StatusCode, body, false, "river_forecast_failed", "River Forecast API returned a non-success status.");
            }

            if (IsCacheable(body))
            {
                await _cache.SetAsync(cacheKey, body, _options.RiverForecastCacheTtl, invocationId, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new RiverForecastApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "River forecast client call failed. InvocationId={InvocationId}", invocationId);
            return new RiverForecastApiResult(0, null, false, "river_forecast_failed", "River Forecast API call failed.");
        }
    }

    private static bool IsCacheable(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("status", out var status))
            {
                return false;
            }

            var value = status.GetString();
            return value is "ok" or "no_reach_within_range" or "no_forecast_for_reach";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static TokenCredential CreateCredential() => new DefaultAzureCredential();
}
