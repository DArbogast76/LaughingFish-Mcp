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

public sealed class ChartedHazardsApiClient : IChartedHazardsApiClient
{
    public const string Path = "/api/v1/wrecks-obstructions";
    public const string Schema = "laughingfish.chartedHazards.v1";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<ChartedHazardsApiClient> _logger;
    private readonly TokenCredential _credential = new DefaultAzureCredential();

    public ChartedHazardsApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<ChartedHazardsApiClient> logger)
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

    public async Task<ChartedHazardsApiResult> GetAsync(
        double latitude,
        double longitude,
        int radiusMiles,
        int limit,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!_options.WrecksApiBound)
        {
            _logger.LogWarning("Charted hazards client skipped. InvocationId={InvocationId} Reason=base_url_unbound", invocationId);
            return new ChartedHazardsApiResult(0, null, false, "charted_hazards_unbound", "WrecksApiBaseUrl is not configured.");
        }

        var lat = Truncate4(latitude);
        var lon = Truncate4(longitude);
        var cacheKey = ChartedHazardsCacheKey.Build(lat, lon, radiusMiles, limit);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "Charted hazards cache hit. InvocationId={InvocationId} Key={Key} RadiusMiles={RadiusMiles} Limit={Limit}",
                invocationId,
                cacheKey,
                radiusMiles,
                limit);
            return new ChartedHazardsApiResult(200, cached.Value, true, null, null);
        }

        var accessToken = await TokenAsync(invocationId, cancellationToken).ConfigureAwait(false);
        if (accessToken is null && _options.WrecksApiAudienceBound)
        {
            return new ChartedHazardsApiResult(0, null, false, "charted_hazards_token_failed", "Could not acquire a token for the wrecks API.");
        }

        var url = $"{_options.WrecksApiBaseUrl!.TrimEnd('/')}{Path}";
        _logger.LogInformation(
            "Charted hazards client request. InvocationId={InvocationId} Path={Path} RadiusMiles={RadiusMiles} Limit={Limit} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            radiusMiles,
            limit,
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { latitude = lat, longitude = lon, radiusMiles, limit }),
                Encoding.UTF8,
                "application/json");
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Charted hazards client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode || !IsPayload(body))
            {
                return new ChartedHazardsApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "charted_hazards_http_error",
                    $"Wrecks API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.WrecksCacheTtl, invocationId, cancellationToken).ConfigureAwait(false);
            return new ChartedHazardsApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Charted hazards client timeout. InvocationId={InvocationId}", invocationId);
            return new ChartedHazardsApiResult(0, null, false, "charted_hazards_timeout", "Wrecks API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Charted hazards client transport failure. InvocationId={InvocationId}", invocationId);
            return new ChartedHazardsApiResult(0, null, false, "charted_hazards_unreachable", "Wrecks API could not be reached.");
        }
    }

    private async Task<string?> TokenAsync(string invocationId, CancellationToken cancellationToken)
    {
        if (!_options.WrecksApiAudienceBound)
        {
            _logger.LogInformation("Charted hazards client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound", invocationId);
            return null;
        }

        try
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext([_options.WrecksApiAudience!]), cancellationToken).ConfigureAwait(false);
            return token.Token;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Charted hazards client token failure. InvocationId={InvocationId}", invocationId);
            return null;
        }
    }

    private static bool IsPayload(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("schema", out var schema)
                && schema.GetString() == Schema;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static double Truncate4(double value) =>
        Math.Truncate(value * 10000d) / 10000d;
}
