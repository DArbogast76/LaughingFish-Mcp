namespace LaughingFish.Mcp.Services;

public interface ISpeciesGuideToolService
{
    Task<SpeciesGuideToolResult> ListSpeciesAsync(string invocationId, CancellationToken cancellationToken);

    Task<SpeciesGuideToolResult> ListTopicsAsync(string invocationId, CancellationToken cancellationToken);

    Task<SpeciesGuideToolResult> SearchAsync(
        string? species,
        string? query,
        string? topic,
        int? top,
        string invocationId,
        CancellationToken cancellationToken);

    Task<SpeciesGuideToolResult> GetChapterAsync(
        string? id,
        string? species,
        string? topic,
        string invocationId,
        CancellationToken cancellationToken);
}
