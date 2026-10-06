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

public sealed class SwimRiskApiClient : ISwimRiskApiClient
{
    public const string Path = "/api/v1/swim-risk";
    public const string Schema = "laughingfish.swimRisk.v1";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<SwimRiskApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public SwimRiskApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<SwimRiskApiClient> logger)
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

    public async Task<SwimRiskApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!_options.SwimRiskApiBound)
        {
            _logger.LogWarning(
                "Swim risk client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new SwimRiskApiResult(0, null, false, "swim_risk_unbound", "SwimRiskApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.SwimRisk(latitude, longitude);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value) && IsSwimRiskPayload(cached.Value))
        {
            _logger.LogInformation(
                "Swim risk client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new SwimRiskApiResult(200, cached.Value, true, null, null);
        }

        var url = $"{_options.SwimRiskApiBaseUrl.TrimEnd('/')}{Path}";
        string? accessToken = null;
        if (_options.SwimRiskApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.SwimRiskApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "Swim risk client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Swim risk client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new SwimRiskApiResult(0, null, false, "swim_risk_token_failed", "Could not acquire a token for the Swim Risk API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "Swim risk client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "Swim risk client request. InvocationId={InvocationId} Path={Path} Latitude={Latitude} Longitude={Longitude} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            latitude,
            longitude,
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                $"{{\"latitude\":{latitude.ToString(CultureInfo.InvariantCulture)},\"longitude\":{longitude.ToString(CultureInfo.InvariantCulture)}}}",
                Encoding.UTF8,
                "application/json");
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Swim risk client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode || !IsSwimRiskPayload(body))
            {
                return new SwimRiskApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "swim_risk_http_error",
                    $"Swim Risk API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.SwimRiskCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new SwimRiskApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Swim risk client timeout. InvocationId={InvocationId}", invocationId);
            return new SwimRiskApiResult(0, null, false, "swim_risk_timeout", "Swim Risk API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Swim risk client transport failure. InvocationId={InvocationId}", invocationId);
            return new SwimRiskApiResult(0, null, false, "swim_risk_unreachable", "Swim Risk API could not be reached.");
        }
    }

    private static bool IsSwimRiskPayload(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("schema", out var schema)
                && schema.GetString() == Schema
                && document.RootElement.TryGetProperty("locations", out var locations)
                && locations.ValueKind == JsonValueKind.Array
                && document.RootElement.TryGetProperty("status", out var status)
                && status.GetString() is "ok" or "no_forecast_within_range";
        }
        catch (JsonException)
        {
            return false;
        }
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
