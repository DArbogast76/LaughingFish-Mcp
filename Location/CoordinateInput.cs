namespace LaughingFish.Mcp.Location;

/// <summary>
/// Required point for every domain tool. Place names are not accepted here.
/// </summary>
public static class CoordinateInput
{
    public static bool TryRead(
        double? latitude,
        double? longitude,
        out double latitudeValue,
        out double longitudeValue,
        out string errorCode,
        out string message)
    {
        if (latitude is null || longitude is null)
        {
            latitudeValue = 0;
            longitudeValue = 0;
            errorCode = "missing_location";
            message = "latitude and longitude are required.";
            return false;
        }

        latitudeValue = latitude.Value;
        longitudeValue = longitude.Value;
        if (latitudeValue is < -90 or > 90
            || longitudeValue is < -180 or > 180
            || double.IsNaN(latitudeValue)
            || double.IsNaN(longitudeValue)
            || double.IsInfinity(latitudeValue)
            || double.IsInfinity(longitudeValue))
        {
            errorCode = "invalid_coordinates";
            message = "latitude must be -90 to 90 and longitude must be -180 to 180.";
            return false;
        }

        errorCode = string.Empty;
        message = string.Empty;
        return true;
    }
}
