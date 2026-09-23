namespace LaughingFish.Mcp.Location;

/// <summary>
/// Shared geocode result. Every tool that accepts a place name uses this type.
/// </summary>
public sealed record ResolvedLocation(
    double Latitude,
    double Longitude,
    string Query,
    string? FormattedAddress,
    string? Locality,
    string? AdminDistrict,
    string? CountryRegion);
