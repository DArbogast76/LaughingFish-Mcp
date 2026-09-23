namespace LaughingFish.Mcp.Location;

/// <summary>
/// Turns a place name, city, or address into coordinates.
/// Inject this into every tool that accepts a location string.
/// </summary>
public interface ILocationResolver
{
    Task<ResolvedLocation> ResolveAsync(
        string query,
        string invocationId,
        CancellationToken cancellationToken);
}
