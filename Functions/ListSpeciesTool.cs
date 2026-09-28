using System.Diagnostics;
using LaughingFish.Mcp.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace LaughingFish.Mcp.Functions;

/// <summary>
/// Lists published Target Species books. No Search hybrid call.
/// </summary>
public sealed class ListSpeciesTool
{
    public const string ToolName = "list_species";
    public const string ToolDescription =
        "Lists published Target Species books with speciesSlug, commonName, and bookTitle. Internal catalog when the named fish is ambiguous or a nickname must be mapped to a published name. Callers never see slugs. Does not return chapter text, live water, weather, or official regulations.";

    private readonly ILogger<ListSpeciesTool> _logger;
    private readonly ISpeciesGuideToolService _service;

    public ListSpeciesTool(ILogger<ListSpeciesTool> logger, ISpeciesGuideToolService service)
    {
        _logger = logger;
        _service = service;
    }

    [Function(nameof(ListSpeciesTool))]
    public async Task<object> Run(
        [McpToolTrigger(ToolName, ToolDescription)] ToolInvocationContext context,
        FunctionContext functionContext)
    {
        var started = Stopwatch.StartNew();
        var invocationId = functionContext.InvocationId;

        _logger.LogInformation(
            "ListSpecies tool started. InvocationId={InvocationId} Tool={Tool} SessionId={SessionId}",
            invocationId,
            context.Name,
            context.SessionId);

        try
        {
            var result = await _service.ListSpeciesAsync(invocationId, functionContext.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "ListSpecies tool finished. InvocationId={InvocationId} StatusCode={StatusCode} ElapsedMs={ElapsedMs}",
                invocationId,
                result.HttpStatus,
                started.ElapsedMilliseconds);

            return result.Body;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "ListSpecies tool failed. InvocationId={InvocationId} ElapsedMs={ElapsedMs}",
                invocationId,
                started.ElapsedMilliseconds);
            throw;
        }
    }
}
