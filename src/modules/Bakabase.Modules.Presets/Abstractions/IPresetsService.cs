using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Sharable;
using Bakabase.Modules.Presets.Abstractions.Models;

namespace Bakabase.Modules.Presets.Abstractions;

public interface IPresetsService
{
    MediaLibraryTemplatePresetDataPool GetMediaLibraryTemplatePresetDataPool();
    Task<int> AddMediaLibrary(MediaLibraryTemplateCompactBuilder mlBuilder);
}