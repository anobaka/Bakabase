using Bakabase.Abstractions.Models.Dto;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerDescriptor
{
    int Id { get; }
    string Name { get; }
    string? Description { get; }
    IEnhancerTargetDescriptor[] Targets { get; }
    int PropertyValueScope { get; }
    IEnhancerTargetDescriptor this[int target] { get; }
}