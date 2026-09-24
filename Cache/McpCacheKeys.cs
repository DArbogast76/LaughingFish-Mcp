using System.Globalization;
using System.Text;

namespace LaughingFish.Mcp.Cache;

/// <summary>
/// Stable Redis keys: lf:{service}:v1:{fingerprint}.
/// Bump Version when a payload shape is incompatible with old entries.
/// </summary>
public static class McpCacheKeys
{
    public const string Prefix = "lf";
    public const string Version = "v1";

    public static string Weather(double latitude, double longitude) =>
        Format("wx", Coord(latitude), Coord(longitude));

    public static string WeatherGrid(string gridId, int gridX, int gridY) =>
        Format("wx", Token(gridId).ToUpperInvariant(), gridX.ToString(CultureInfo.InvariantCulture), gridY.ToString(CultureInfo.InvariantCulture));

    public static string Sunrise(double latitude, double longitude, string date, string? timeZone) =>
        Format("ss", Coord(latitude), Coord(longitude), Token(date), string.IsNullOrWhiteSpace(timeZone) ? "-" : Token(timeZone));

    public static string WaterTemperature(double latitude, double longitude, int nearest, int days, int maxDistanceMiles) =>
        Format(
            "wt",
            Coord(latitude),
            Coord(longitude),
            nearest.ToString(CultureInfo.InvariantCulture),
            days.ToString(CultureInfo.InvariantCulture),
            maxDistanceMiles.ToString(CultureInfo.InvariantCulture));

    public static string Maps(string place) =>
        Format("maps", NormalizePlace(place));

    private static string Format(string service, params string[] parts) =>
        $"{Prefix}:{service}:{Version}:{string.Join(':', parts)}";

    private static string Coord(double value) =>
        Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var trimmed = value.Trim();
        return trimmed.Replace(':', '_');
    }

    private static string NormalizePlace(string place)
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return "-";
        }

        var builder = new StringBuilder(place.Length);
        var pendingSpace = false;
        foreach (var ch in place.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0)
            {
                builder.Append('_');
                pendingSpace = false;
            }

            builder.Append(ch == ':' ? '_' : ch);
        }

        return builder.Length == 0 ? "-" : builder.ToString();
    }
}
