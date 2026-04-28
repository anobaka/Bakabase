using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.AI.Components.Tools;
using Microsoft.Extensions.AI;

namespace Bakabase.Service.Components.AI.Tools;

public class ResourcePropertyTool(IResourceService resourceService) : ILlmTool
{
    [Description(
        "Modify a property value of a resource. Use isBizValue=true when providing human-readable values (like labels for choices), or false for raw database values.")]
    public async Task<string> SetResourcePropertyValue(
        [Description("The resource ID")] int resourceId,
        [Description("The property ID")] int propertyId,
        [Description("Whether this is a custom property (true) or built-in (false)")] bool isCustomProperty,
        [Description("The property value as a string")] string? value,
        [Description("If true, value is treated as a human-readable biz value")] bool isBizValue = true)
    {
        var model = new ResourcePropertyValuePutInputModel
        {
            PropertyId = propertyId,
            IsCustomProperty = isCustomProperty,
            Value = value,
            IsBizValue = isBizValue,
        };
        var result = await resourceService.PutPropertyValue(resourceId, model);
        return result.Code == 0
            ? JsonSerializer.Serialize(new { Success = true })
            : JsonSerializer.Serialize(new { Success = false, Error = result.Message });
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SetResourcePropertyValue);
    }

    public IEnumerable<LlmToolMetadata> GetMetadata()
    {
        yield return new LlmToolMetadata { Name = "SetResourcePropertyValue", Description = "Modify a resource property value", IsReadOnly = false };
    }
}
