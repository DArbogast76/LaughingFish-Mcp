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

public sealed class TideNowApiClient : ITideNowApiClient
{
    public const string Path = "/api/v1/tide-now";
    public const string Schema = "laughingfish.tideNow.v1";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<TideNowApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public TideNowApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<TideNowApiClient> logger)
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

    public async Task<TideNowApiResult> GetAsync(
        double latitude,
        double longitude,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (!_options.TideNowApiBound)
        {
            _logger.LogWarning(
                "Tide now client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new TideNowApiResult(0, null, false, "tide_now_unbound", "TideNowApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.TideNow(latitude, longitude);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value) && IsTideNowPayload(cached.Value))
        {
            _logger.LogInformation(
                "Tide now client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new TideNowApiResult(200, cached.Value, true, null, null);
        }

        var url = $"{_options.TideNowApiBaseUrl.TrimEnd('/')}{Path}";
        string? accessToken = null;
        if (_options.TideNowApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.TideNowApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "Tide now client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Tide now client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new TideNowApiResult(0, null, false, "tide_now_token_failed", "Could not acquire a token for the Tide Now API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "Tide now client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "Tide now client request. InvocationId={InvocationId} Path={Path} Latitude={Latitude} Longitude={Longitude} BearerAttached={BearerAttached}",
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
                "Tide now client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode || !IsTideNowPayload(body))
            {
                return new TideNowApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "tide_now_http_error",
                    $"Tide Now API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.TideNowCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new TideNowApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Tide now client timeout. InvocationId={InvocationId}", invocationId);
            return new TideNowApiResult(0, null, false, "tide_now_timeout", "Tide Now API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Tide now client transport failure. InvocationId={InvocationId}", invocationId);
            return new TideNowApiResult(0, null, false, "tide_now_unreachable", "Tide Now API could not be reached.");
        }
    }

    private static bool IsTideNowPayload(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("schema", out var schema)
                && schema.GetString() == Schema
                && document.RootElement.TryGetProperty("observation", out var observation)
                && observation.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("status", out var status)
                && status.GetString() is "ok" or "no_station_within_range";
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
