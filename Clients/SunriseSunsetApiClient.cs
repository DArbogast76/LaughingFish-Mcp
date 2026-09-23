using System.Net.Http.Headers;
using LaughingFish.Mcp.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaughingFish.Mcp.Clients;

public sealed class SunriseSunsetApiClient : ISunriseSunsetApiClient
{
    public const string Path = "/api/v1/sunrise-sunset";

    private readonly HttpClient _http;
    private readonly McpOptions _options;
    private readonly ILogger<SunriseSunsetApiClient> _logger;

    public SunriseSunsetApiClient(
        HttpClient http,
        IOptions<McpOptions> options,
        ILogger<SunriseSunsetApiClient> logger)
    {
        _http = http;
        _options = options.Value;
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

        var baseUrl = _options.SunriseSunsetApiBaseUrl.TrimEnd('/');
        var query = $"lat={Uri.EscapeDataString(latitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&lon={Uri.EscapeDataString(longitude.ToString(System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&date={Uri.EscapeDataString(date)}";
        if (!string.IsNullOrWhiteSpace(timeZone))
        {
            query += $"&timeZone={Uri.EscapeDataString(timeZone.Trim())}";
        }

        var url = $"{baseUrl}{Path}?{query}";

        _logger.LogInformation(
            "SunriseSunset client request. InvocationId={InvocationId} Path={Path} Lat={Lat} Lon={Lon} Date={Date} TimeZone={TimeZone}",
            invocationId,
            Path,
            latitude,
            longitude,
            date,
            timeZone);

        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
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
}
