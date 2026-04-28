using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Components.Tools;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class ResourceEnhancerTool(IEnhancerService enhancerService) : ILlmTool
{
    [Description(
        "Run a single enhancer against one resource. Results are written to the enhancement targets (resource properties) defined by that enhancer and the media library template. " +
        "Before calling: configure the enhancer in system settings (cookies, API keys, options, and which properties receive values). " +
        "If prerequisites are missing, the run may fail; direct the user to Enhancer / third-party settings in the app when needed.")]
    public async Task<string> RunEnhancerOnResource(
        [Description("Resource ID")] int resourceId,
        [Description("Numeric enhancer ID from the user's enhancer list")] int enhancerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await enhancerService.EnhanceResource(resourceId, [enhancerId], PauseToken.None, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                Success = true,
                Message =
                    $"Enhancer {enhancerId} finished for resource {resourceId}. Open resource details to inspect applied properties.",
            });
        }
        catch (System.Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Error = ex.Message });
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(RunEnhancerOnResource);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata
        {
            Name = "RunEnhancerOnResource",
            Description = "Run one enhancer on a single resource (writes to configured enhancement targets)",
            IsReadOnly = false,
        };
    }
}
