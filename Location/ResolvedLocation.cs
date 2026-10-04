namespace LaughingFish.Mcp.Location;

/// <summary>
/// Shared geocode result. Every tool that accepts a place name uses this type.
/// PostalCode is the US ZIP from the same Maps response, when Maps returned one.
/// </summary>
public sealed record ResolvedLocation(
    double Latitude,
    double Longitude,
    string Query,
    string? FormattedAddress,
    string? Locality,
    string? AdminDistrict,
    string? CountryRegion,
    string? PostalCode = null);
