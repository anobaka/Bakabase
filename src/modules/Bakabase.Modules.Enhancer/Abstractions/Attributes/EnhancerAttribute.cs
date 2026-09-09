using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class EnhancerAttribute(
    Type enhancerType,
    PropertyValueScope propertyValueScope,
    Type targetEnumType,
    EnhancerTag[] tags,
    bool requiresLocalFiles = false) : Attribute
{
    public Type EnhancerType { get; } = enhancerType;
    public Type TargetEnumType { get; } = targetEnumType;
    public int PropertyValueScope { get; } = (int) propertyValueScope;
    public EnhancerTag[] Tags { get; } = tags;

    /// <summary>
    /// Whether this enhancer reads the resource's files — its path, the names inside it, the media
    /// it contains. Such an enhancer has nothing to work with on a resource that is known to
    /// Bakabase but not materialized on disk yet, and running it there would store an empty result
    /// that is never retried, permanently blocking it from running once the files do arrive.
    /// </summary>
    public bool RequiresLocalFiles { get; } = requiresLocalFiles;
}