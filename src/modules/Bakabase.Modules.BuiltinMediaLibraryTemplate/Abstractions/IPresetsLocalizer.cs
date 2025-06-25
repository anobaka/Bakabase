using Bakabase.Modules.Presets.Abstractions.Models.Constants;

namespace Bakabase.Modules.Presets.Abstractions;

public interface IPresetsLocalizer
{
    string? ResourceTypeDescription(PresetResourceType resourceType);
}