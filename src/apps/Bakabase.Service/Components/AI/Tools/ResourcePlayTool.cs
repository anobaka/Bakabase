using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.AI.Components.Tools;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class ResourcePlayTool(IResourceService resourceService) : ILlmTool
{
    [Description("Play/open a specific resource by its ID. This opens the resource file with the system default player.")]
    public async Task<string> PlayResource(
        [Description("The resource ID to play")] int resourceId)
    {
        var result = await resourceService.Play(resourceId, string.Empty);
        return result.Code == 0
            ? JsonSerializer.Serialize(new { Success = true, Message = $"Started playing resource {resourceId}" })
            : JsonSerializer.Serialize(new { Success = false, Error = result.Message });
    }

    [Description("Play a random resource from the library. Good for when the user wants a surprise or random pick.")]
    public async Task<string> PlayRandomResource()
    {
        var result = await resourceService.PlayRandomResource();
        return result.Code == 0
            ? JsonSerializer.Serialize(new { Success = true, Message = "Started playing a random resource" })
            : JsonSerializer.Serialize(new { Success = false, Error = result.Message });
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(PlayResource);
        yield return AIFunctionFactory.Create(PlayRandomResource);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "PlayResource", Description = "Play/open a resource", IsReadOnly = false };
        yield return new LlmToolMetadata { Name = "PlayRandomResource", Description = "Play a random resource", IsReadOnly = false };
    }
}
