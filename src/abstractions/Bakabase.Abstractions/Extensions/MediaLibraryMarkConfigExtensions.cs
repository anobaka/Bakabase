using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class MediaLibraryMarkConfigExtensions
{
    public static PathMatchMode GetEffectiveValueMatchMode(this MediaLibraryMarkConfig config) =>
        config.ValueMatchMode ?? config.MatchMode;
}
