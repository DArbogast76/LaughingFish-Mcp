using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Merges query string and JSON body for tool HTTP APIs. Body wins on key clash.
/// </summary>
internal static class ToolHttpRequest
{
    public static async Task<Dictionary<string, JsonElement>> ReadAsync(
        HttpRequest req,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in req.Query)
        {
            var raw = pair.Value.ToString();
            values[pair.Key] = JsonSerializer.SerializeToElement(raw);
        }

        if (!HttpMethods.IsPost(req.Method) && !HttpMethods.IsPut(req.Method))
        {
            return values;
        }

        if (req.ContentLength is 0)
        {
            return values;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(req.Body, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return values;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = property.Value.Clone();
            }
        }
        catch (JsonException)
        {
            // Query-only request stands. Caller validation reports missing fields.
        }

        return values;
    }

    public static string? ReadString(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    public static int? ReadInt(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
