namespace LaughingFish.Mcp.Functions;

/// <summary>
/// HTTP tool catalog. Add a row in the same change that adds or changes an HTTP tool route.
/// Routes come from the HTTP function constants so a renamed route cannot drift.
/// MCP-only tools stay in <see cref="NotOnHttp"/> and are not given a path.
/// </summary>
public static class ToolCatalog
{
    public const string Schema = "laughingfish.mcp.toolCatalog.v1";
    public const string Path = "/v1/tools";

    public static IReadOnlyList<ToolCatalogEntry> All { get; } =
    [
        Species("list_species", "listSpecies", SpeciesGuideHttp.ListSpeciesRoute,
            "Returns the published species list. No inputs. Does not return chapter text.",
            [],
            "ok true and the published species names and slugs.",
            null,
            "/v1/tools/list-species"),
        Species("list_guide_topics", "listGuideTopics", SpeciesGuideHttp.ListTopicsRoute,
            "Returns the published topic keys. No inputs. Does not return chapter text.",
            [],
            "ok true and the published topic keys.",
            null,
            "/v1/tools/list-guide-topics"),
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
            "/v1/tools/search-species-guides?species=redfish&query=where%20they%20hold&top=3"),
        Species("get_chapter", "getChapter", SpeciesGuideHttp.ChapterRoute,
            "Returns one published chapter. Pass id, or pass species and topic. Does not search.",
            [
                Input("id", false, "string", "Chapter id. Example: haddock-time-temperature. Required when species and topic are omitted."),
                Input("species", false, "string", "Published common name or slug. Required with topic when id is omitted."),
                Input("topic", false, "string", "Published topic key. Required with species when id is omitted.")
            ],
            "ok true and one chapter body.",
            null,
            "/v1/tools/get-chapter?species=haddock&topic=time-temperature"),
        Conditions("get_sunrise_sunset", "getSunriseSunset", SunriseSunsetHttp.Route,
            "Returns sunrise, sunset, dawn, dusk, civil twilight, and solar noon for a date. Latitude and longitude are required. date is yyyy-MM-dd and defaults to today's UTC date. timeZone is an optional IANA or Windows id. This is not weather and not water temperature.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("date", false, "string", "yyyy-MM-dd. Default is today's UTC date."),
                Input("timeZone", false, "string", "Optional IANA or Windows time zone id. Example: America/New_York.")
            ],
            "ok true, the resolved location, and the solar times.",
            null,
            ["missing_location", "invalid_coordinates", "invalid_date"],
            "/v1/tools/get-sunrise-sunset?latitude=38.9784&longitude=-76.4922&date=2026-10-06"),
        Conditions("get_weather_forecast", "getWeatherForecast", WeatherForecastHttp.Route,
            "Returns an hourly air weather forecast: temperature, rain, wind, humidity, sky cover, and conditions. Latitude and longitude are required. hours is 24, 48, or 72. days is 1, 3, or 7. Do not send both. Omit both for 24 hours. This is a forecast, not a current observation.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("hours", false, "integer", "24, 48, or 72. Do not send with days. Default 24 when both are omitted."),
                Input("days", false, "integer", "1, 3, or 7. Do not send with hours.")
            ],
            "ok true, hoursRequested, hoursReturned, and the forecast hours.",
            null,
            ["missing_location", "invalid_coordinates", "invalid_window"],
            "/v1/tools/get-weather-forecast?latitude=38.9784&longitude=-76.4922&days=1"),
        Conditions("get_water_temperature", "getWaterTemperature", WaterTemperatureHttp.Route,
            "Returns observed water temperature: the current reading and hourly history for trend. Days is lookback, not a forecast. Latitude and longitude are required. nearest is 1, 3, or 5 and defaults to 1. days is 1, 3, 7, 30, or 90 and defaults to 1. maxDistanceMiles is 10, 25, or 50 and defaults to 25. This route returns JSON only and does not stream a chart image.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("nearest", false, "integer", "1, 3, or 5. Default 1."),
                Input("days", false, "integer", "1, 3, 7, 30, or 90. Default 1. Lookback, not a forecast."),
                Input("maxDistanceMiles", false, "integer", "10, 25, or 50. Default 25.")
            ],
            "ok true and the observed readings for the stations in range.",
            "no_station_within_range is a successful empty result.",
            ["missing_location", "invalid_coordinates", "invalid_nearest", "invalid_days", "invalid_max_distance"],
            "/v1/tools/get-water-temperature?latitude=38.9784&longitude=-76.4922&days=7"),
        Conditions("get_charted_hazards", "getChartedHazards", ChartedHazardsHttp.Route,
            "Returns charted wrecks and obstructions inside a radius of a latitude and longitude. Both coordinates are required. radiusMiles is an integer from 1 to 50 and defaults to 2. It is the search radius, not the size of a wreck. limit is 1 to 100 and defaults to 25. Hazards are nearest first. kind is wreck or obstruction. distanceMiles and distanceMeters are from the requested point to the charted point. leastDepthMeters is the charted sounding. leastDepthFeet is that depth in feet. waterLevel says whether the chart shows the point covered or exposed. wreckCategory is the charted type, such as dangerous wreck, foul ground, crib, fish haven, or wellhead. chartCell is not useful in an answer.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("radiusMiles", false, "integer", "1 to 50. Default 2. Search radius, not a wreck size."),
                Input("limit", false, "integer", "1 to 100. Default 25. Nearest are kept.")
            ],
            "ok true and the charted hazards nearest the point. truncated true means farther points inside the radius were left out.",
            "An empty hazards list means no charted wreck or obstruction point was found in range. An empty list is not a failed call.",
            ["invalid_request", "charted_hazards_unavailable", "charted_hazards_invalid_body"],
            "/v1/tools/get-charted-hazards?latitude=38.9784&longitude=-76.4922&radiusMiles=2"),
        Conditions("get_lunar_cycle", "getLunarCycle", LunarCycleHttp.Route,
            "Returns the lunar almanac for a local date range. Latitude and longitude are required. Pass date, or startDate and endDate. Omit the date for today. The range is inclusive and at most 31 days. Do not send a time zone. This is not a tide prediction.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("date", false, "string", "yyyy-MM-dd. Omit for today. Do not send with startDate or endDate."),
                Input("startDate", false, "string", "yyyy-MM-dd. Inclusive. Required with endDate when date is omitted."),
                Input("endDate", false, "string", "yyyy-MM-dd. Inclusive. At most 31 days from startDate.")
            ],
            "ok true, the resolved location, and days of lunar phase and illumination.",
            null,
            ["missing_location", "invalid_coordinates", "invalid_date"],
            "/v1/tools/get-lunar-cycle?latitude=38.9784&longitude=-76.4922&date=2026-10-06"),
        Conditions("get_tide_predictions", "getTidePredictions", TidePredictionsHttp.Route,
            "Returns predicted high and low turns for a date range. Latitude and longitude are required. Pass start and end as yyyy-MM-dd, or omit both for today. Inclusive, at most 31 days. Do not send a time zone. nearest is 1, 3, or 5 and defaults to 1. maxDistanceMiles is 10, 25, or 50 and defaults to 25. Heights are predicted above Mean Lower Low Water, in feet and meters. type H is a high tide. type L is a low tide. This is not an observed water level and not a continuous curve.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("start", false, "string", "yyyy-MM-dd. Omit with end for today.", ["startDate"]),
                Input("end", false, "string", "yyyy-MM-dd. Inclusive. At most 31 days from start.", ["endDate"]),
                Input("nearest", false, "integer", "1, 3, or 5. Default 1."),
                Input("maxDistanceMiles", false, "integer", "10, 25, or 50. Default 25.")
            ],
            "ok true and predicted high and low events with local and UTC times.",
            "no_station_within_range and no_predictions_in_window are successful empty results.",
            ["missing_location", "invalid_coordinates", "invalid_window", "invalid_nearest", "invalid_max_distance"],
            "/v1/tools/get-tide-predictions?latitude=38.9784&longitude=-76.4922&start=2026-10-06&end=2026-10-07"),
        Conditions("get_sea_conditions", "getSeaConditions", SeaConditionsHttp.Route,
            "Returns observed sea conditions at the nearest reporting stations: significant wave height, dominant and average period, wave direction, sustained wind, gust, barometric pressure, and station air temperature. Recent history, not a forecast. Latitude and longitude are required. nearest is 1, 3, or 5 and defaults to 3. days is trailing UTC hours and is 1, 3, 7, 45, or 90. Default 1. days is not a date range and not a forecast. maxDistanceMiles is 10, 25, or 50 and defaults to 50.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"]),
                Input("nearest", false, "integer", "1, 3, or 5. Default 3."),
                Input("days", false, "integer", "1, 3, 7, 45, or 90. Default 1. Trailing UTC hours."),
                Input("maxDistanceMiles", false, "integer", "10, 25, or 50. Default 50.")
            ],
            "ok true and station observations. sensors says which of wave height, wave direction, wind, and pressure that station reports.",
            "no_station_within_range and no_readings_in_window are successful empty results.",
            ["missing_location", "invalid_coordinates", "invalid_nearest", "invalid_days", "invalid_max_distance"],
            "/v1/tools/get-sea-conditions?latitude=36.8529&longitude=-75.9780&nearest=3&days=1"),
        Conditions("get_uv_index", "getUvIndex", UvIndexHttp.Route,
            "Returns the current EPA UV Index issuance for a five-digit US ZIP. Hourly values and the daily index are the issuance published now. No date is accepted and no later day is available. zip is required. A point is not accepted.",
            [
                Input("zip", true, "string", "Five-digit US ZIP. ZIP+4 keeps the first five digits.")
            ],
            "ok true and the current EPA issuance, including the outdoor scale.",
            "An issuance with no forecast is a structured error, not an invented index.",
            ["invalid_zip", "uv_unbound", "uv_invalid_body"],
            "/v1/tools/get-uv-index?zip=21401"),
        Conditions("get_weather_alerts", "getWeatherAlerts", WeatherAlertsHttp.Route,
            "Returns National Weather Service watches, warnings, and advisories active at the moment of the call. Snapshot, not a forecast and not a history. Latitude and longitude are both required. Each alert includes event, severity, urgency, certainty, response, headline, description, instruction, area description, effective, expires, and ends. expires is when the message expires. ends is when the hazard ends, when it was sent. Relay instruction when present. When instruction is empty, relay description. Do not invent safety guidance.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"])
            ],
            "ok true and the active Actual alerts for that point, sorted by severity then expires.",
            "An empty alerts array means no active alert contains that point, including points outside National Weather Service coverage. An empty array is not a failed call.",
            ["invalid_request", "weather_alerts_invalid_body"],
            "/v1/tools/get-weather-alerts?latitude=30.4213&longitude=-87.2169"),
        Conditions("get_swim_risk", "getSwimRisk", SwimRiskHttp.Route,
            "Returns the issued National Weather Service surf-zone rip current risk for forecast zones intersecting a latitude and longitude. Issued forecast, not a measured observation and not a rating for one beach. Latitude and longitude are both required. There is no place name, date, or radius. Search uses 10 miles, then 25 miles only when the smaller search has no rated zone. Day 1 is 1200 UTC today through 1200 UTC tomorrow. Day 2 is the next 1200 UTC window. rip is Low, Moderate, or High. beachname is the zone, and the rating covers that whole zone. Product date and time are office text as issued, not UTC.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"])
            ],
            "ok true and the rated forecast zones intersecting that point, with Day 1 and Day 2 when issued.",
            "no_forecast_within_range means no rated zone within 25 miles. An empty locations array is not a failed call and not a statement that the water is safe.",
            ["invalid_request", "swim_risk_invalid_body", "swim_risk_unbound"],
            "/v1/tools/get-swim-risk?latitude=36.85&longitude=-75.98"),
        Conditions("get_surf_forecast", "getSurfForecast", SurfForecastHttp.Route,
            "Returns the issued National Weather Service breaking-surf height for a latitude and longitude. A surf-zone forecast, not a measured wave height and not offshore seas. Latitude and longitude are both required. There is no place name, date, or radius. The map search uses 10 miles, then 25 miles only when the smaller search has no issued surf text. source beachSummary returns map zones in locations. source surfZoneForecast returns the text segment for the point's forecast zone in surfZoneForecast.rows and leaves locations empty. text is the height as issued. minFeet and maxFeet are set only for one height or one simple range.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"])
            ],
            "ok true and either map zones in locations or text rows in surfZoneForecast.",
            "no_forecast_within_range means no issued map surf text and no text rows for this point's forecast zone. An empty result is not a failed call and not a statement that the water is calm.",
            ["invalid_request", "surf_forecast_invalid_body", "surf_forecast_unbound"],
            "/v1/tools/get-surf-forecast?latitude=36.85&longitude=-75.98"),
        Conditions("get_tides", "getTides", TideNowHttp.Route,
            "Latest measured water-surface height at the nearest CO-OPS gauge within 100 miles. Latitude and longitude are both required. There is no place name, date, or station id. heightFeet and heightMeters are the newest sample above the datum. MLLW is the coastal and tidal-river chart zero. LWD is the Great Lakes chart zero. coverage level_and_direction means direction is the change from the prior sample. coverage current_level_only means only one sample was returned, so direction is null. A null direction is not steady.",
            [
                Input("latitude", true, "number", "-90 to 90.", ["lat"]),
                Input("longitude", true, "number", "-180 to 180.", ["lon"])
            ],
            "ok true and the nearest gauge sample, or a null observation when no gauge in range returned a sample.",
            "no_station_within_range means no gauge within 100 miles returned a recent sample. That is a successful result, not a failed call, and it does not mean the water is flat.",
            ["invalid_request", "tide_now_invalid_body", "tide_now_unbound"],
            "/v1/tools/get-tides?latitude=38.9784&longitude=-76.4922")
    ];

    public static IReadOnlyList<ToolCatalogGap> NotOnHttp { get; } =
    [
        new("resolve_location", "MCP only. No HTTP route."),
        new("server_health", "MCP only. Host smoke test is GET /health and is not this tool.")
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
    public string Path => "/" + Route;
}

public sealed record ToolCatalogGap(string Name, string Reason);
