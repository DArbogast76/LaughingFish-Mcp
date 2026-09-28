using System.Net;
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

    public static SpeciesGuideToolResult FromApi(
        SpeciesGuideApiResult result,
        string invocationId)
    {
        if (!result.IsSuccess)
        {
            var status = result.StatusCode switch
            {
                0 => StatusCodes.Status502BadGateway,
                (int)HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                (int)HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                (int)HttpStatusCode.Unauthorized => StatusCodes.Status502BadGateway,
                (int)HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
                _ when result.StatusCode is >= 400 and < 500 => result.StatusCode,
                _ => StatusCodes.Status502BadGateway
            };

            return new SpeciesGuideToolResult(status, new
            {
                ok = false,
                error = result.ErrorCode,
                statusCode = result.StatusCode == 0 ? (int?)null : result.StatusCode,
                invocationId
            });
        }

        var api = ParseNode(result.Body);
        var payload = new JsonObject
        {
            ["ok"] = true,
            ["invocationId"] = invocationId,
            ["result"] = api
        };
        if (api is JsonObject obj && obj["body"] is JsonNode story)
        {
            payload["body"] = story.DeepClone();
            if (obj["title"] is JsonNode title)
            {
                payload["title"] = title.DeepClone();
            }
        }

        return new SpeciesGuideToolResult(StatusCodes.Status200OK, payload);
    }

    public static SpeciesGuideToolResult Empty(string invocationId) =>
        new(StatusCodes.Status502BadGateway, new
        {
            ok = false,
            error = "species_guide_unavailable",
            invocationId
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
