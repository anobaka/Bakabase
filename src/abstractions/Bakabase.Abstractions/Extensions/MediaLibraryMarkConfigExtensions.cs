using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryMarkConfigExtensions
{
    public static List<int> GetFixedMediaLibraryIds(this MediaLibraryMarkConfig config) =>
        (config.MediaLibraryIds ?? (config.MediaLibraryId.HasValue ? [config.MediaLibraryId.Value] : []))
        .Where(id => id > 0).Distinct().ToList();

    public static PathMatchMode GetEffectiveValueMatchMode(this MediaLibraryMarkConfig config) =>
        config.ValueMatchMode ?? config.MatchMode;
}
