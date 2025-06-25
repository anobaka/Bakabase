using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Presets.Abstractions.Models.Constants;
using Microsoft.Extensions.Localization;

namespace Bakabase.Modules.Presets.Components;

internal class PresetsLocalizer(IStringLocalizer<PresetsResource> localizer) : IPresetsLocalizer
{
    public string? ResourceTypeDescription(PresetResourceType resourceType)
    {
        var desc = localizer[$"PresetResourceTypeDescription_{resourceType}"];
        return desc.ResourceNotFound ? null : (string) desc;
    }
}