using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

/// <summary>
/// Proxies Target Species book search. Never calls Azure AI Search or embeddings directly.
/// </summary>
public sealed class SpeciesGuideApiClient : ISpeciesGuideApiClient
{
    public const string SpeciesPath = "/api/v1/species-guides/species";
    public const string TopicsPath = "/api/v1/species-guides/topics";
    public const string SearchPath = "/api/v1/species-guides/search";
    public const string ChaptersPath = "/api/v1/species-guides/chapters";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<SpeciesGuideApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public SpeciesGuideApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<SpeciesGuideApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.4"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<SpeciesGuideApiResult> ListSpeciesAsync(
        string invocationId,
        CancellationToken cancellationToken) =>
        GetAsync(SpeciesPath, McpCacheKeys.SpeciesGuideCatalog(), "species", invocationId, cancellationToken);

    public Task<SpeciesGuideApiResult> ListTopicsAsync(
        string invocationId,
        CancellationToken cancellationToken) =>
        GetAsync(TopicsPath, McpCacheKeys.SpeciesGuideTopics(), "topics", invocationId, cancellationToken);

    public Task<SpeciesGuideApiResult> SearchAsync(
        string species,
        string? query,
        string? topic,
        int top,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var slug = NormalizeSlug(species);
        var topicKey = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim().ToLowerInvariant();
        var queryText = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var queryPart = new List<string>
        {
            $"species={Uri.EscapeDataString(slug)}"
        };
        if (queryText is not null)
        {
            queryPart.Add($"query={Uri.EscapeDataString(queryText)}");
        }

        if (topicKey is not null)
        {
            queryPart.Add($"topic={Uri.EscapeDataString(topicKey)}");
        }

        queryPart.Add($"top={top}");

        var path = $"{SearchPath}?{string.Join('&', queryPart)}";
        var cacheKey = McpCacheKeys.SpeciesGuideSearch(slug, topicKey, Fingerprint(queryText), top);
        return GetAsync(path, cacheKey, "search", invocationId, cancellationToken);
    }

    public Task<SpeciesGuideApiResult> GetChapterAsync(
        string? id,
        string? species,
        string? topic,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var queryPart = new List<string>();
        if (!string.IsNullOrWhiteSpace(id))
        {
            queryPart.Add($"id={Uri.EscapeDataString(id.Trim().ToLowerInvariant())}");
        }

        if (!string.IsNullOrWhiteSpace(species))
        {
            queryPart.Add($"species={Uri.EscapeDataString(NormalizeSlug(species))}");
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            queryPart.Add($"topic={Uri.EscapeDataString(topic.Trim().ToLowerInvariant())}");
        }

        var path = $"{ChaptersPath}?{string.Join('&', queryPart)}";
        var cacheKey = McpCacheKeys.SpeciesGuideChapter(
            string.IsNullOrWhiteSpace(id) ? null : id.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(species) ? null : NormalizeSlug(species),
            string.IsNullOrWhiteSpace(topic) ? null : topic.Trim().ToLowerInvariant());
        return GetAsync(path, cacheKey, "chapter", invocationId, cancellationToken);
    }

    private async Task<SpeciesGuideApiResult> GetAsync(
        string pathAndQuery,
        string cacheKey,
        string operation,
        string invocationId,
        CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(_options.SpeciesGuideApiBaseUrl);
        if (baseUrl is null)
        {
            _logger.LogWarning(
                "SpeciesGuide client skipped. InvocationId={InvocationId} Operation={Operation} Reason=base_url_unbound",
                invocationId,
                operation);
            return new SpeciesGuideApiResult(
                0,
                null,
                false,
                "species_guide_unbound",
                null);
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning(
                "SpeciesGuide client skipped. InvocationId={InvocationId} Operation={Operation} Reason=base_url_invalid Length={Length}",
                invocationId,
                operation,
                _options.SpeciesGuideApiBaseUrl.Trim().Length);
            return new SpeciesGuideApiResult(
                0,
                null,
                false,
                "species_guide_invalid_base_url",
                null);
        }

        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "SpeciesGuide client cache hit. InvocationId={InvocationId} Operation={Operation} Key={Key}",
                invocationId,
                operation,
                cacheKey);
            return new SpeciesGuideApiResult(200, cached.Value, true, null, null);
        }

        var url = $"{baseUrl}{pathAndQuery}";

        string? accessToken = null;
        if (_options.SpeciesGuideApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.SpeciesGuideApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "SpeciesGuide client token acquired. InvocationId={InvocationId} Operation={Operation} ElapsedMs={ElapsedMs}",
                    invocationId,
                    operation,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SpeciesGuide client token failure. InvocationId={InvocationId} Operation={Operation} ElapsedMs={ElapsedMs}",
                    invocationId,
                    operation,
                    tokenStarted.ElapsedMilliseconds);
                return new SpeciesGuideApiResult(
                    0,
                    null,
                    false,
                    "species_guide_token_failed",
                    null);
            }
        }
        else
        {
            _logger.LogWarning(
                "SpeciesGuide client sending unauthenticated request. InvocationId={InvocationId} Operation={Operation} Reason=audience_unbound",
                invocationId,
                operation);
        }

        _logger.LogInformation(
            "SpeciesGuide client request. InvocationId={InvocationId} Operation={Operation} Path={Path} BearerAttached={BearerAttached}",
            invocationId,
            operation,
            pathAndQuery,
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
                "SpeciesGuide client response. InvocationId={InvocationId} Operation={Operation} StatusCode={StatusCode} BodyLength={BodyLength} ElapsedMs={ElapsedMs}",
                invocationId,
                operation,
                (int)response.StatusCode,
                body.Length,
                sendStarted.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new SpeciesGuideApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    response.StatusCode == System.Net.HttpStatusCode.NotFound
                        ? "species_guide_not_found"
                        : "species_guide_http_error",
                    null);
            }

            await _cache.SetAsync(cacheKey, body, _options.SpeciesGuideCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new SpeciesGuideApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "SpeciesGuide client timeout. InvocationId={InvocationId} Operation={Operation} ElapsedMs={ElapsedMs} CallerCanceled={CallerCanceled}",
                invocationId,
                operation,
                sendStarted.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            return new SpeciesGuideApiResult(0, null, false, "species_guide_timeout", null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "SpeciesGuide client transport failure. InvocationId={InvocationId} Operation={Operation}",
                invocationId,
                operation);
            return new SpeciesGuideApiResult(
                0,
                null,
                false,
                "species_guide_unreachable",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SpeciesGuide client failed. InvocationId={InvocationId} Operation={Operation}",
                invocationId,
                operation);
            return new SpeciesGuideApiResult(
                0,
                null,
                false,
                "species_guide_client_error",
                null);
        }
    }

    private static readonly Regex EmbeddedHttpUrl = new(
        @"https?://[^\s""'<>\u201C\u201D\u2018\u2019]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            '\u200C',
            '\u200D',
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
        if (match.Success && TryAbsoluteHttp(match.Value.TrimEnd('/'), out var embedded))
        {
            return embedded;
        }

        return null;
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

    internal static string NormalizeSlug(string species)
    {
        var trimmed = species.Trim().ToLowerInvariant();
        var builder = new StringBuilder(trimmed.Length);
        var pendingDash = false;
        foreach (var ch in trimmed)
        {
            if (ch is ' ' or '_' or '-')
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (!char.IsLetterOrDigit(ch))
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (pendingDash)
            {
                builder.Append('-');
                pendingDash = false;
            }

            builder.Append(ch);
        }

        return builder.Length == 0 ? trimmed : builder.ToString();
    }

    private static string Fingerprint(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "-";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(query.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
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
