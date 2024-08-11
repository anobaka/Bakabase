using Bakabase.Abstractions.Exceptions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components;

public class EnhancerDescriptor(
    EnhancerId enhancerId,
    IEnhancerLocalizer localizer,
    IEnumerable<IEnhancerTargetDescriptor> targets,
    int propertyValueScope) : IEnhancerDescriptor
{
    public int Id { get; } = (int) enhancerId;
    public string Name => localizer.Enhancer_Name(enhancerId);
    public string? Description => localizer.Enhancer_Description(enhancerId);
    public IEnhancerTargetDescriptor[] Targets { get; } = targets.ToArray();
    public int PropertyValueScope { get; } = propertyValueScope;

    public IEnhancerTargetDescriptor this[int target]
    {
        get
        {
            var td = Targets.FirstOrDefault(x => x.Id == target);
            if (td == null)
            {
                throw new DevException($"{nameof(EnhancerTargetDescriptor)}:{target} of {enhancerId} is not found");
            }

            return td;
        }
    }
}