using System.Text.Json;
using System.Text.Json.Nodes;
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
            return Empty(invocationId);
        }

        return new SpeciesGuideToolResult(StatusCodes.Status200OK, new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["result"] = ParseNode(result.Body)
        });
    }

    public static SpeciesGuideToolResult Empty(string invocationId) =>
        new(StatusCodes.Status200OK, new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["result"] = new JsonObject
            {
                ["status"] = "ok",
                ["count"] = 0
            }
        });

    private static JsonNode ParseNode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(body) ?? new JsonObject();
        }
        catch (JsonException)
        {
            return JsonValue.Create(body)!;
        }
    }
}
