using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components;

internal record EnhancerDescriptors : IEnhancerDescriptors
{
    private readonly Dictionary<int, IEnhancerDescriptor> _descriptors;

    public EnhancerDescriptors(IEnhancerDescriptor[] descriptors, IEnhancerLocalizer localizer)
    {
        _descriptors = descriptors.ToDictionary(d => d.Id, d => d);
        Descriptors = descriptors;
    }

    public IEnhancerDescriptor[] Descriptors { get; }

    public IEnhancerDescriptor? TryGet(int enhancerId) => _descriptors.TryGetValue(enhancerId, out var descriptor) ? descriptor : null;

    public IEnhancerDescriptor this[int enhancerId]
    {
        get
        {
            var descriptor = TryGet(enhancerId);
            return descriptor ?? throw new DevException($"{nameof(EnhancerDescriptor)} for {enhancerId} is not found");
        }
    }
}