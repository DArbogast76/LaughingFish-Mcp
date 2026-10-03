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
/// Proxies stored sea-condition observations. Never calls NDBC and never reads sea-condition tables.
/// </summary>
public sealed class SeaConditionsApiClient : ISeaConditionsApiClient
{
    public const string Path = "/api/v1/sea-conditions";

    private static readonly Regex EmbeddedHttpUrl = new(
        @"https?://[^\s""'<>\u201C\u201D\u2018\u2019]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<SeaConditionsApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public SeaConditionsApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<SeaConditionsApiClient> logger)
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

    public async Task<SeaConditionsApiResult> GetAsync(
        double latitude,
        double longitude,
        int nearest,
        int days,
        int maxDistanceMiles,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(_options.SeaConditionsApiBaseUrl);
        if (baseUrl is null)
        {
            _logger.LogWarning(
                "Sea conditions client skipped. InvocationId={InvocationId} Reason=base_url_unbound Length={Length}",
                invocationId,
                _options.SeaConditionsApiBaseUrl?.Trim().Length ?? 0);
            return new SeaConditionsApiResult(0, null, false, "sea_conditions_unbound", null);
        }

        var cacheKey = McpCacheKeys.SeaConditions(latitude, longitude, nearest, days, maxDistanceMiles);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "Sea conditions client cache hit. InvocationId={InvocationId} Key={Key}",
                invocationId,
                cacheKey);
            return new SeaConditionsApiResult(200, cached.Value, true, null, null);
        }

        var query = $"latitude={Uri.EscapeDataString(latitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&longitude={Uri.EscapeDataString(longitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&nearest={nearest.ToString(CultureInfo.InvariantCulture)}"
            + $"&days={days.ToString(CultureInfo.InvariantCulture)}"
            + $"&maxDistanceMiles={maxDistanceMiles.ToString(CultureInfo.InvariantCulture)}";
        var url = $"{baseUrl}{Path}?{query}";

        string? accessToken = null;
        if (_options.SeaConditionsApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.SeaConditionsApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "Sea conditions client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Sea conditions client token failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new SeaConditionsApiResult(0, null, false, "sea_conditions_token_failed", null);
            }
        }

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
                "Sea conditions client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength} ElapsedMs={ElapsedMs}",
                invocationId,
                (int)response.StatusCode,
                body.Length,
                sendStarted.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new SeaConditionsApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "sea_conditions_not_found"
                        : "sea_conditions_http_error",
                    null);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.LogWarning(
                    "Sea conditions client empty success body. InvocationId={InvocationId} StatusCode={StatusCode}",
                    invocationId,
                    (int)response.StatusCode);
                return new SeaConditionsApiResult((int)response.StatusCode, body, false, "sea_conditions_empty", null);
            }

            await _cache.SetAsync(cacheKey, body, _options.SeaConditionsCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new SeaConditionsApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Sea conditions client timeout. InvocationId={InvocationId} ElapsedMs={ElapsedMs} CallerCanceled={CallerCanceled}",
                invocationId,
                sendStarted.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            return new SeaConditionsApiResult(0, null, false, "sea_conditions_timeout", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Sea conditions client transport failure. InvocationId={InvocationId}",
                invocationId);
            return new SeaConditionsApiResult(0, null, false, "sea_conditions_unreachable", null);
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
