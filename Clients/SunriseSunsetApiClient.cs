using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class SunriseSunsetApiClient : ISunriseSunsetApiClient
{
    public const string Path = "/api/v1/sunrise-sunset";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<SunriseSunsetApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public SunriseSunsetApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<SunriseSunsetApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.2"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<SunriseSunsetApiResult> GetAsync(
        double latitude,
        double longitude,
        string date,
        string? timeZone,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SunriseSunsetApiBaseUrl))
        {
            _logger.LogWarning(
                "SunriseSunset client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new SunriseSunsetApiResult(
                0,
                null,
                false,
                "sunrise_sunset_unbound",
                "SunriseSunsetApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.Sunrise(latitude, longitude, date, timeZone);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "SunriseSunset client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new SunriseSunsetApiResult(200, cached.Value, true, null, null);
        }

        var baseUrl = _options.SunriseSunsetApiBaseUrl.TrimEnd('/');
        var query = $"lat={Uri.EscapeDataString(latitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&lon={Uri.EscapeDataString(longitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&date={Uri.EscapeDataString(date)}";
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            query += $"&timeZone={Uri.EscapeDataString(timeZone.Trim())}";
        }

        var url = $"{baseUrl}{Path}?{query}";

        string? accessToken = null;
        if (_options.SunriseSunsetApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.SunriseSunsetApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "SunriseSunset client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SunriseSunset client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new SunriseSunsetApiResult(
                    0,
                    null,
                    false,
                    "sunrise_sunset_token_failed",
                    "Could not acquire a token for the SunriseSunset API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "SunriseSunset client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "SunriseSunset client request. InvocationId={InvocationId} Path={Path} Lat={Lat} Lon={Lon} Date={Date} TimeZone={TimeZone} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            latitude,
            longitude,
            date,
            timeZone,
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
                "SunriseSunset client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength}",
                invocationId,
                (int)response.StatusCode,
                body.Length);

            if (!response.IsSuccessStatusCode)
            {
                return new SunriseSunsetApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "sunrise_sunset_http_error",
                    $"SunriseSunset API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.SunriseCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new SunriseSunsetApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "SunriseSunset client timeout. InvocationId={InvocationId}",
                invocationId);
            return new SunriseSunsetApiResult(0, null, false, "sunrise_sunset_timeout", "SunriseSunset API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "SunriseSunset client transport failure. InvocationId={InvocationId}",
                invocationId);
            return new SunriseSunsetApiResult(0, null, false, "sunrise_sunset_unreachable", "SunriseSunset API could not be reached.");
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
