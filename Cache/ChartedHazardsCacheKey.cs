using System.Globalization;

namespace LaughingFish.Mcp.Cache;

public static class ChartedHazardsCacheKey
{
    public static string Build(double latitude, double longitude, int radiusMiles, int limit) =>
        $"lf:wrecks:v1:expl1:{Coord(latitude)}:{Coord(longitude)}:{radiusMiles.ToString(CultureInfo.InvariantCulture)}:{limit.ToString(CultureInfo.InvariantCulture)}";

    private static string Coord(double value) =>
        Math.Round(value, 4).ToString("0.0000", CultureInfo.InvariantCulture);
}
