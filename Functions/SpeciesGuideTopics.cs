namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Published Target Species topic keys. Matches the Species Guide API topics list.
/// </summary>
internal static class SpeciesGuideTopics
{
    internal static readonly string[] All =
    [
        "general",
        "science",
        "habitat-behavior",
        "gear-tackle",
        "techniques",
        "hotspots",
        "time-temperature",
        "reading-water",
        "tips-tricks",
        "conservation",
        "boat-control",
        "electronics",
        "related-media",
        "sizes-records",
        "consumption-recipes",
        "fictional-story"
    ];

    internal static bool IsPublished(string topic) =>
        All.Contains(topic, StringComparer.Ordinal);
}
