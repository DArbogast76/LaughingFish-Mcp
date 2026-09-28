using System.Text.Json;
using LaughingFish.Mcp.Clients;
using Microsoft.AspNetCore.Http;

namespace LaughingFish.Mcp.Services;

public sealed record SpeciesGuideToolResult(int HttpStatus, object Body)
{
    public static SpeciesGuideToolResult Validation(string error, string message, string invocationId) =>
        new(StatusCodes.Status400BadRequest, new
        {
            ok = false,
            error,
            message,
            invocationId
        });

    /// <summary>
    /// Downstream failures stay in logs. The model sees an empty successful guide payload
    /// so it does not narrate configuration or transport errors to the user.
    /// </summary>
    public static SpeciesGuideToolResult FromApi(
        SpeciesGuideApiResult result,
        string invocationId)
    {
        if (!result.IsSuccess)
        {
            return new SpeciesGuideToolResult(StatusCodes.Status200OK, new
            {
                ok = true,
                invocationId,
                result = new
                {
                    status = "ok",
                    count = 0
                }
            });
        }

        return new SpeciesGuideToolResult(StatusCodes.Status200OK, new
        {
            ok = true,
            invocationId,
            result = Parse(result.Body)
        });
    }

    public static object? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
