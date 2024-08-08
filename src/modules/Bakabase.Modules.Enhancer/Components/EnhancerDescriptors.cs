using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Components;

public record EnhancerDescriptors : IEnhancerDescriptors
{
    private readonly Dictionary<int, IEnhancerDescriptor> _descriptors;

    public EnhancerDescriptors(IEnhancerDescriptor[] descriptors, IEnhancerLocalizer localizer)
    {
        _descriptors = descriptors.ToDictionary(d => d.Id, d => d);
        Descriptors = descriptors;
    }

    public IEnhancerDescriptor[] Descriptors { get; }

    public IEnhancerDescriptor? TryGet(int id) => _descriptors.TryGetValue(id, out var descriptor) ? descriptor : null;

    public IEnhancerDescriptor this[int id]
    {
        get
        {
            var descriptor = TryGet(id);
            return descriptor ?? throw new DevException($"{nameof(EnhancerDescriptor)} for {id} is not found");
        }
    }
}