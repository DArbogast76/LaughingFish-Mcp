using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class WeatherApiClient : IWeatherApiClient
{
    public const string Path = "/api/v1/weather/forecast";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly ILogger<WeatherApiClient> _logger;
    private readonly TokenCredential _credential = CreateCredential();

    public WeatherApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        ILogger<WeatherApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        var timeoutSeconds = _options.WeatherApiTimeoutSeconds > 0 ? _options.WeatherApiTimeoutSeconds : 90;
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LaughingFish-Mcp", "0.3"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<WeatherApiResult> GetForecastAsync(
        double latitude,
        double longitude,
        int hourCount,
        string invocationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WeatherApiBaseUrl))
        {
            _logger.LogWarning(
                "Weather client skipped. InvocationId={InvocationId} Reason=base_url_unbound",
                invocationId);
            return new WeatherApiResult(
                0,
                null,
                false,
                "weather_unbound",
                "WeatherApiBaseUrl is not configured.",
                null,
                hourCount);
        }

        var baseUrl = _options.WeatherApiBaseUrl.TrimEnd('/');
        var query = $"lat={Uri.EscapeDataString(latitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&lon={Uri.EscapeDataString(longitude.ToString(CultureInfo.InvariantCulture))}"
            + $"&hours={hourCount.ToString(CultureInfo.InvariantCulture)}";
        var url = $"{baseUrl}{Path}?{query}";

        string? accessToken = null;
        if (_options.WeatherApiAudienceBound)
        {
            var tokenStarted = Stopwatch.StartNew();
            try
            {
                var audience = _options.WeatherApiAudience.Trim().TrimEnd('/');
                var scope = audience.EndsWith("/.default", StringComparison.OrdinalIgnoreCase)
                    ? audience
                    : $"{audience}/.default";
                var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken)
                    .ConfigureAwait(false);
                accessToken = token.Token;
                _logger.LogInformation(
                    "Weather client token acquired. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Weather client token failure. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                    invocationId,
                    tokenStarted.ElapsedMilliseconds);
                return new WeatherApiResult(
                    0,
                    null,
                    false,
                    "weather_token_failed",
                    "Could not acquire a token for the Weather API.",
                    null,
                    hourCount);
            }
        }
        else
        {
            _logger.LogWarning(
                "Weather client sending unauthenticated request. InvocationId={InvocationId} Reason=audience_unbound",
                invocationId);
        }

        _logger.LogInformation(
            "Weather client request. InvocationId={InvocationId} Path={Path} Lat={Lat} Lon={Lon} HourCount={HourCount} BearerAttached={BearerAttached} TimeoutSeconds={TimeoutSeconds}",
            invocationId,
            Path,
            latitude,
            longitude,
            hourCount,
            accessToken is not null,
            (int)_http.Timeout.TotalSeconds);

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
                "Weather client response. InvocationId={InvocationId} StatusCode={StatusCode} BodyLength={BodyLength} ElapsedMs={ElapsedMs}",
                invocationId,
                (int)response.StatusCode,
                body.Length,
                sendStarted.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                return new WeatherApiResult(
                    (int)response.StatusCode,
                    body,
                    false,
                    "weather_http_error",
                    $"Weather API returned {(int)response.StatusCode}.",
                    null,
                    hourCount);
            }

            var trimmed = TrimHours(body, hourCount, invocationId, out var returned);
            return new WeatherApiResult((int)response.StatusCode, trimmed, true, null, null, returned, hourCount);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                ex,
                "Weather client timeout. InvocationId={InvocationId} ElapsedMs={ElapsedMs} CallerCanceled={CallerCanceled}",
                invocationId,
                sendStarted.ElapsedMilliseconds,
                cancellationToken.IsCancellationRequested);
            return new WeatherApiResult(0, null, false, "weather_timeout", "Weather API timed out.", null, hourCount);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Weather client transport failure. InvocationId={InvocationId}", invocationId);
            return new WeatherApiResult(
                0,
                null,
                false,
                "weather_unreachable",
                "Weather API could not be reached.",
                null,
                hourCount);
        }
    }

    private string TrimHours(string body, int hourCount, string invocationId, out int returned)
    {
        returned = hourCount;
        try
        {
            var node = JsonNode.Parse(body);
            if (node is not JsonObject root || node["hours"] is not JsonArray hours)
            {
                _logger.LogInformation(
                    "Weather client trim skipped. InvocationId={InvocationId} Reason=hours_array_missing",
                    invocationId);
                return body;
            }

            var original = hours.Count;
            while (hours.Count > hourCount)
            {
                hours.RemoveAt(hours.Count - 1);
            }

            returned = hours.Count;
            if (root["window"] is JsonObject window)
            {
                window["hoursReturned"] = returned;
                if (hours.Count > 0 && hours[^1]?["tUtc"] is JsonValue last)
                {
                    window["endUtc"] = last.GetValue<string>();
                }
            }

            _logger.LogInformation(
                "Weather client trimmed hours. InvocationId={InvocationId} OriginalHours={OriginalHours} ReturnedHours={ReturnedHours}",
                invocationId,
                original,
                returned);

            return root.ToJsonString();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Weather client trim failed. InvocationId={InvocationId}",
                invocationId);
            return body;
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
