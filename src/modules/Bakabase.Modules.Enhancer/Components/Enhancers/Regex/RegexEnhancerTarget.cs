using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Components.EnhancementConverters;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Regex;

public enum RegexEnhancerTarget
{
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty], true)]
    CaptureGroups,
}