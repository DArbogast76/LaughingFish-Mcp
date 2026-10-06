namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP tool catalog. Add a row in the same change that adds or changes an HTTP tool route.
/// Routes come from the HTTP function constants so a renamed route cannot drift.
/// MCP-only tools stay in <see cref="NotOnHttp"/> and are not given a path.
/// </summary>
public static class ToolCatalog
{
    public const string Schema = "laughingfish.mcp.toolCatalog.v1";
    public const string Path = "/api/v1/tools";

    public static IReadOnlyList<ToolCatalogEntry> All { get; } =
    [
        Species("list_species", "listSpecies", SpeciesGuideHttp.ListSpeciesRoute,
            "Returns the published species list. No inputs. Does not return chapter text.",
            [],
            "ok true and the published species names and slugs.",
            null,
            "/api/v1/tools/list-species"),
        Species("list_guide_topics", "listGuideTopics", SpeciesGuideHttp.ListTopicsRoute,
            "Returns the published topic keys. No inputs. Does not return chapter text.",
            [],
            "ok true and the published topic keys.",
            null,
            "/api/v1/tools/list-guide-topics"),
        Species("search_species_guides", "searchSpeciesGuides", SpeciesGuideHttp.SearchRoute,
            "Returns how-to and species facts from one published book. Required species is a published common name or slug. Put the question in query. topic is an optional published topic key. top is 1, 2, or 3 and defaults to 3. Does not return live water or weather, and does not return regulations.",
            [
                Input("species", true, "string", "Published common name or slug. One species per call."),
                Input("query", false, "string", "Question about that species. Optional."),
                Input("topic", false, "string", "Published topic key. Optional."),
                Input("top", false, "integer", "1, 2, or 3. Default 3.")
            ],
            "ok true and the matching chapter bodies.",
            null,
            "/api/v1/tools/search-species-guides?species=redfish&query=where%20they%20hold&top=3"),
        Species("get_chapter", "getChapter", SpeciesGuideHttp.ChapterRoute,
            "Returns one published chapter. Pass id, or pass species and topic. Does not search.",
            [
                Input("id", false, "string", "Chapter id. Example: haddock-time-temperature. Required when species and topic are omitted."),
                Input("species", false, "string", "Published common name or slug. Required with topic when id is omitted."),
                Input("topic", false, "string", "Published topic key. Required with species when id is omitted.")
            ],
            "ok true and one chapter body.",
            null,
            "/api/v1/tools/get-chapter?species=haddock&topic=time-temperature"),
        Conditions("get_lunar_cycle", "getLunarCycle", LunarCycleHttp.Route,
            "Returns the lunar almanac for a place and local date range. Pass place, or latitude and longitude. Pass date, or startDate and endDate. Omit the date for today. The range is inclusive and at most 31 days. Do not send a time zone. This is not a tide prediction.",
            [
                Input("place", false, "string", "Place name. Required when latitude and longitude are omitted.", null),
                Input("latitude", false, "number", "-90 to 90. Required with longitude when place is omitted.", ["lat"]),
                Input("longitude", false, "number", "-180 to 180. Required with latitude when place is omitted.", ["lon"]),
                Input("date", false, "string", "yyyy-MM-dd. Omit for today. Do not send with startDate or endDate."),
                Input("startDate", false, "string", "yyyy-MM-dd. Inclusive. Required with endDate when date is omitted."),
                Input("endDate", false, "string", "yyyy-MM-dd. Inclusive. At most 31 days from startDate.")
            ],
            "ok true, the resolved location, and days of lunar phase and illumination.",
            null,
            ["missing_location", "invalid_coordinates", "invalid_date"],
            "/api/v1/tools/get-lunar-cycle?place=Annapolis%2C%20MD&date=2026-10-06"),
        Conditions("get_tide_predictions", "getTidePredictions", TidePredictionsHttp.Route,
            "Returns predicted high and low turns for a place and date range. Pass place, or latitude and longitude. Pass start and end as yyyy-MM-dd, or omit both for today. Inclusive, at most 31 days. Do not send a time zone. nearest is 1, 3, or 5 and defaults to 1. maxDistanceMiles is 10, 25, or 50 and defaults to 25. Heights are predicted above Mean Lower Low Water, in feet and meters. type H is a high tide. type L is a low tide. This is not an observed water level and not a continuous curve.",
            [
                Input("place", false, "string", "Place name. Required when latitude and longitude are omitted."),
                Input("latitude", false, "number", "-90 to 90. Required with longitude when place is omitted.", ["lat"]),
                Input("longitude", false, "number", "-180 to 180. Required with latitude when place is omitted.", ["lon"]),
                Input("start", false, "string", "yyyy-MM-dd. Omit with end for today.", ["startDate"]),
                Input("end", false, "string", "yyyy-MM-dd. Inclusive. At most 31 days from start.", ["endDate"]),
                Input("nearest", false, "integer", "1, 3, or 5. Default 1."),
                Input("maxDistanceMiles", false, "integer", "10, 25, or 50. Default 25.")
            ],
            "ok true and predicted high and low events with local and UTC times.",
            "no_station_within_range and no_predictions_in_window are successful empty results.",
            ["missing_location", "invalid_coordinates", "invalid_window", "invalid_nearest", "invalid_max_distance"],
            "/api/v1/tools/get-tide-predictions?place=Annapolis%2C%20MD&start=2026-10-06&end=2026-10-07"),
        Conditions("get_sea_conditions", "getSeaConditions", SeaConditionsHttp.Route,
            "Returns observed sea conditions at the nearest reporting stations: significant wave height, dominant and average period, wave direction, sustained wind, gust, barometric pressure, and station air temperature. Recent history, not a forecast. Pass place, or latitude and longitude. nearest is 1, 3, or 5 and defaults to 3. days is trailing UTC hours and is 1, 3, 7, 45, or 90. Default 1. days is not a date range and not a forecast. maxDistanceMiles is 10, 25, or 50 and defaults to 50.",
            [
                Input("place", false, "string", "Place name. Required when latitude and longitude are omitted."),
                Input("latitude", false, "number", "-90 to 90. Required with longitude when place is omitted.", ["lat"]),
                Input("longitude", false, "number", "-180 to 180. Required with latitude when place is omitted.", ["lon"]),
                Input("nearest", false, "integer", "1, 3, or 5. Default 3."),
                Input("days", false, "integer", "1, 3, 7, 45, or 90. Default 1. Trailing UTC hours."),
                Input("maxDistanceMiles", false, "integer", "10, 25, or 50. Default 50.")
            ],
            "ok true and station observations. sensors says which of wave height, wave direction, wind, and pressure that station reports.",
            "no_station_within_range and no_readings_in_window are successful empty results.",
            ["missing_location", "invalid_coordinates", "invalid_nearest", "invalid_days", "invalid_max_distance"],
            "/api/v1/tools/get-sea-conditions?place=Virginia%20Beach%2C%20VA&nearest=3&days=1"),
        Conditions("get_uv_index", "getUvIndex", UvIndexHttp.Route,
            "Returns the current EPA UV Index issuance for a US place or five-digit ZIP. Hourly values and the daily index are the issuance published now. No date is accepted and no later day is available. Pass place or zip. Latitude and longitude are not accepted.",
            [
                Input("place", false, "string", "US place name. Required when zip is omitted."),
                Input("zip", false, "string", "Five-digit US ZIP. Required when place is omitted.")
            ],
            "ok true and the current EPA issuance, including the outdoor scale.",
            "A place with no ZIP, or an issuance with no forecast, is a structured error, not an invented index.",
            ["missing_location", "invalid_zip", "uv_unbound", "uv_invalid_body"],
            "/api/v1/tools/get-uv-index?zip=21401"),
        Conditions("get_weather_alerts", "getWeatherAlerts", WeatherAlertsHttp.Route,
            "Returns National Weather Service watches, warnings, and advisories active at the moment of the call. Snapshot, not a forecast and not a history. Latitude and longitude are both required. Each alert includes event, severity, urgency, certainty, response, headline, description, instruction, area description, effective, expires, and ends. expires is when the message expires. ends is when the hazard ends, when it was sent. Relay instruction when present. When instruction is empty, relay description. Do not invent safety guidance.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"])
            ],
            "ok true and the active Actual alerts for that point, sorted by severity then expires.",
            "An empty alerts array means no active alert contains that point, including points outside National Weather Service coverage. An empty array is not a failed call.",
            ["invalid_request", "weather_alerts_invalid_body"],
            "/api/v1/tools/get-weather-alerts?latitude=30.4213&longitude=-87.2169")
    ];

    public static IReadOnlyList<ToolCatalogGap> NotOnHttp { get; } =
    [
        new("resolve_location", "MCP only. No HTTP route."),
        new("get_sunrise_sunset", "MCP only. No HTTP route."),
        new("get_weather_forecast", "MCP only. No HTTP route."),
        new("get_water_temperature", "MCP only. No HTTP route."),
        new("server_health", "MCP only. Host smoke test is GET /api/health and is not this tool.")
    ];

    public static IReadOnlyDictionary<string, string> PathMap() =>
        All.ToDictionary(entry => entry.HealthKey, entry => entry.Path, StringComparer.Ordinal);

    private static ToolCatalogEntry Species(
        string name,
        string healthKey,
        string route,
        string instruction,
        IReadOnlyList<ToolCatalogInput> inputs,
        string success,
        string? emptySuccess,
        string example) =>
        new(name, healthKey, route, instruction, inputs, success, emptySuccess,
            ["The HTTP status follows the tool result: 200 on success, 400 for a bad request, 404 when the chapter or species is missing, 502 when the downstream call fails."],
            example);

    private static ToolCatalogEntry Conditions(
        string name,
        string healthKey,
        string route,
        string instruction,
        IReadOnlyList<ToolCatalogInput> inputs,
        string success,
        string? emptySuccess,
        IReadOnlyList<string> errors,
        string example) =>
        new(name, healthKey, route, instruction, inputs, success, emptySuccess, errors, example);

    private static ToolCatalogInput Input(string name, bool required, string type, string rule, IReadOnlyList<string>? aliases = null) =>
        new(name, required, type, rule, aliases);
}

public sealed record ToolCatalogInput(
    string Name,
    bool Required,
    string Type,
    string Rule,
    IReadOnlyList<string>? Aliases);

public sealed record ToolCatalogEntry(
    string Name,
    string HealthKey,
    string Route,
    string Instruction,
    IReadOnlyList<ToolCatalogInput> Inputs,
    string Success,
    string? EmptySuccess,
    IReadOnlyList<string> Errors,
    string Example)
{
    public string Path => "/api/" + Route;
}

public sealed record ToolCatalogGap(string Name, string Reason);
