using System.Net;
using System.Text.Json;
using LaughingFish.Mcp.Clients;
using LaughingFish.Mcp.Configuration;
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
        McpOptions options,
        string invocationId)
    {
        object? payload = Parse(result.Body);
        if (!result.IsSuccess)
        {
            var status = result.StatusCode switch
            {
                0 => StatusCodes.Status502BadGateway,
                (int)HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                (int)HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
                (int)HttpStatusCode.Unauthorized => StatusCodes.Status502BadGateway,
                (int)HttpStatusCode.Forbidden => StatusCodes.Status502BadGateway,
                _ when result.StatusCode >= 400 && result.StatusCode < 500 => result.StatusCode,
                _ => StatusCodes.Status502BadGateway
            };

            return new SpeciesGuideToolResult(status, new
            {
                ok = false,
                error = result.ErrorCode,
                message = result.ErrorMessage,
                statusCode = result.StatusCode == 0 ? (int?)null : result.StatusCode,
                invocationId,
                speciesGuideApiBound = options.SpeciesGuideApiBound,
                speciesGuideApiAudienceBound = options.SpeciesGuideApiAudienceBound,
                body = payload
            });
        }

        return new SpeciesGuideToolResult(StatusCodes.Status200OK, new
        {
            ok = true,
            invocationId,
            source = "LaughingFish.SpeciesGuideApi",
            statusCode = result.StatusCode,
            result = payload
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
