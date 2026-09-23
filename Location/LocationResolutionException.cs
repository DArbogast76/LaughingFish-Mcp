namespace LaughingFish.Mcp.Location;

public sealed class LocationResolutionException : Exception
{
    public LocationResolutionException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
