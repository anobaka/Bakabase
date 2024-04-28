using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public interface IEnhancerDescriptor
{
    EnhancerId Id { get; }
    string Name { get; }
    string? Description { get; }
    EnhancerTargetDescriptor[] Targets { get; }
}