using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.AI;

public enum AiEnhancerTarget
{
    [EnhancerTarget(StandardValueType.ListString, PropertyType.MultipleChoice,
        [EnhancerTargetOptionsItem.AutoBindProperty], true)]
    Properties = 1,
}
