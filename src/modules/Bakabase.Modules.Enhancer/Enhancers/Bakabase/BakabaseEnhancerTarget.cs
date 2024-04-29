using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;

namespace Bakabase.Modules.Enhancer.Enhancers.Bakabase;

public enum BakabaseEnhancerTarget
{
    [EnhancerTarget(StandardValueType.String)]
    Name = 1,
    [EnhancerTarget(StandardValueType.ListString)]
    Publisher,
    [EnhancerTarget(StandardValueType.DateTime)]
    ReleaseDt,
    [EnhancerTarget(StandardValueType.String)]
    VolumeName,
    [EnhancerTarget(StandardValueType.String)]
    VolumeTitle,
    [EnhancerTarget(StandardValueType.ListString)]
    Originals,
    [EnhancerTarget(StandardValueType.String)]
    Language
}