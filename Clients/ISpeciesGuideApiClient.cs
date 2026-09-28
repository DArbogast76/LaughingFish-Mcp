namespace LaughingFish.Mcp.Clients;

public sealed record SpeciesGuideApiResult(
    int StatusCode,
    string? Body,
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISpeciesGuideApiClient
{
    Task<SpeciesGuideApiResult> ListSpeciesAsync(
        string invocationId,
        CancellationToken cancellationToken);

    Task<SpeciesGuideApiResult> ListTopicsAsync(
        string invocationId,
        CancellationToken cancellationToken);

    Task<SpeciesGuideApiResult> SearchAsync(
        string species,
        string? query,
        string? topic,
        int top,
        string invocationId,
        CancellationToken cancellationToken);

    Task<SpeciesGuideApiResult> GetChapterAsync(
        string? id,
        string? species,
        string? topic,
        string invocationId,
        CancellationToken cancellationToken);
}
