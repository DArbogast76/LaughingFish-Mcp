using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

/// <summary>
/// Proxies the in-process Lunar Cycle calculator. Never calls an external moon service.
/// </summary>
public sealed class LunarCycleApiClient : ILunarCycleApiClient
{
    public const string Path = "/api/v1/lunar-cycle";

    private static readonly Regex EmbeddedHttpUrl = new(
        @"https?://[^\s""'<>\u201C\u201D\u2018\u2019]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<LunarCycleApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public LunarCycleApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<LunarCycleApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.5"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<LunarCycleApiResult> GetAsync(
        double latitude,
        double longitude,
        string startDate,
        string endDate,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(_options.LunarCycleApiBaseUrl);
        if (baseUrl is null)
        {
            _logger.LogWarning(
                "LunarCycle client skipped. InvocationId={InvocationId} Reason=base_url_unbound Length={Length}",
                invocationId,
                _options.LunarCycleApiBaseUrl?.Trim().Length ?? 0);
            return new LunarCycleApiResult(0, null, false, "lunar_cycle_unbound", null);
        }

        var cacheKey = McpCacheKeys.LunarCycle(latitude, longitude, startDate, endDate);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "LunarCycle client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new LunarCycleApiResult(200, cached.Value, true, null, null);
        }

        var query = $"lat={Uri.EscapeDataString(latitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&lon={Uri.EscapeDataString(longitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&startDate={Uri.EscapeDataString(startDate)}"
            + $"&endDate={Uri.EscapeDataString(endDate)}";
        var url = $"{baseUrl}{Path}?{query}";

        string? accessToken = null;
        if (_options.LunarCycleApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.LunarCycleApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "LunarCycle client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "LunarCycle client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new LunarCycleApiResult(0, null, false, "lunar_cycle_token_failed", null);
            }
        }
        else
        {
            _logger.LogInformation(
                "LunarCycle client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "LunarCycle client request. InvocationId={InvocationId} Path={Path} Lat={Lat} Lon={Lon} StartDate={StartDate} EndDate={EndDate} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            latitude,
            longitude,
            startDate,
            endDate,
            accessToken is not null);

        var sendStarted = Stopwatch.StartNew();
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
                "LunarCycle client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength} ElapsedMs={ElapsedMs}",
                invocationId,
                (int)response.StatusCode,
                body.Length,
                sendStarted.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new LunarCycleApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "lunar_cycle_not_found"
                        : "lunar_cycle_http_error",
                    null);
            }

            await _cache.SetAsync(cacheKey, body, _options.LunarCycleCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new LunarCycleApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "LunarCycle client timeout. InvocationId={InvocationId} ElapsedMs={ElapsedMs} CallerCanceled={CallerCanceled}",
                invocationId,
                sendStarted.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            return new LunarCycleApiResult(0, null, false, "lunar_cycle_timeout", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "LunarCycle client transport failure. InvocationId={InvocationId}",
                invocationId);
            return new LunarCycleApiResult(0, null, false, "lunar_cycle_unreachable", null);
        }
    }

    internal static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().Trim(
            ' ',
            '\t',
            '\r',
            '\n',
            '"',
            '\'',
            '\uFEFF',
            '\u200B',
            '\u201C',
            '\u201D',
            '\u2018',
            '\u2019');
        if (value.Length == 0)
        {
            return null;
        }

        if (TryAbsoluteHttp(value, out var direct))
        {
            return direct;
        }

        if (!value.Contains("://", StringComparison.Ordinal)
            && TryAbsoluteHttp("https://" + value.TrimStart('/'), out var prefixed))
        {
            return prefixed;
        }

        var match = EmbeddedHttpUrl.Match(raw);
        return match.Success && TryAbsoluteHttp(match.Value.TrimEnd('/'), out var embedded)
            ? embedded
            : null;
    }

    private static bool TryAbsoluteHttp(string value, out string? normalized)
    {
        normalized = null;
        if (!Uri.TryCreate(value.TrimEnd('/'), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(normalized);
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
