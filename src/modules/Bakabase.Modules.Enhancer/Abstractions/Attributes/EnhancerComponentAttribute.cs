using Bakabase.Abstractions.Components.Component;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Attributes;

public class EnhancerComponentAttribute : ComponentAttribute
{
    public override ComponentType Type => ComponentType.Enhancer;
}