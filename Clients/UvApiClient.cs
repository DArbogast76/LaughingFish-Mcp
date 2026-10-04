using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Cache;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class UvApiClient : IUvApiClient
{
    public const string Path = "/api/v1/uv-index";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly IMcpCache _cache;
    private readonly ILogger<UvApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public UvApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        IMcpCache cache,
        ILogger<UvApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _cache = cache;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.4"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<UvApiResult> GetAsync(
        string zip,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.UvApiBaseUrl))
        {
            _logger.LogWarning(
                "UV client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new UvApiResult(0, null, false, "uv_unbound", "UvApiBaseUrl is not configured.");
        }

        var cacheKey = McpCacheKeys.Uv(zip);
        var cached = await _cache.GetAsync(cacheKey, invocationId, cancellationToken).ConfigureAwait(false);
        if (cached.Hit && !string.IsNullOrWhiteSpace(cached.Value))
        {
            _logger.LogInformation(
                "UV client cache hit. InvocationId={InvocationId} Key={Key} Zip={Zip}",
                invocationId,
                cacheKey,
                zip);
            return new UvApiResult(200, cached.Value, true, null, null);
        }

        var baseUrl = _options.UvApiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}{Path}";
        string? accessToken = null;
        if (_options.UvApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.UvApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "UV client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "UV client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new UvApiResult(0, null, false, "uv_token_failed", "Could not acquire a token for the UV API.");
            }
        }
        else
        {
            _logger.LogWarning(
                "UV client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "UV client request. InvocationId={InvocationId} Path={Path} Zip={Zip} BearerAttached={BearerAttached}",
            invocationId,
            Path,
            zip,
            accessToken is not null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent($"{{\"zip\":\"{zip}\"}}", Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "UV client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength} Zip={Zip}",
                invocationId,
                (int)response.StatusCode,
                body.Length,
                zip);

            if (!response.IsSuccessStatusCode)
            {
                return new UvApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "uv_http_error",
                    $"UV API returned {(int)response.StatusCode}.");
            }

            await _cache.SetAsync(cacheKey, body, _options.UvCacheTtl, invocationId, cancellationToken)
                .ConfigureAwait(false);
            return new UvApiResult((int)response.StatusCode, body, true, null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "UV client timeout. InvocationId={InvocationId} Zip={Zip}", invocationId, zip);
            return new UvApiResult(0, null, false, "uv_timeout", "UV API timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "UV client transport failure. InvocationId={InvocationId} Zip={Zip}", invocationId, zip);
            return new UvApiResult(0, null, false, "uv_unreachable", "UV API could not be reached.");
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
