using System.Collections.Frozen;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Enhancer.Components;

/// <summary>
/// Answers what an enhancer needs from a resource before it can say anything useful about it.
/// Declared on <see cref="EnhancerAttribute"/> and read here so callers do not each re-do the
/// attribute lookup.
/// </summary>
public static class EnhancerRequirements
{
    private static readonly FrozenDictionary<int, bool> RequiresLocalFilesMap =
        SpecificEnumUtils<EnhancerId>.Values.ToFrozenDictionary(
            id => (int)id,
            id => id.GetAttribute<EnhancerAttribute>()?.RequiresLocalFiles ?? false);

    /// <summary>
    /// Whether the enhancer reads the resource's files. Running such an enhancer against a resource
    /// with no local files produces an empty result that is stored and never retried, which would
    /// block it from ever running once the files arrive.
    /// </summary>
    public static bool RequiresLocalFiles(int enhancerId) =>
        RequiresLocalFilesMap.GetValueOrDefault(enhancerId);
}
