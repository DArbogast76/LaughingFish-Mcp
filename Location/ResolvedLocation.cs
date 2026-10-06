namespace LaughingFish.Mcp.Location;

/// <summary>
/// Geocode result returned by resolve_location. Domain tools do not geocode.
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
