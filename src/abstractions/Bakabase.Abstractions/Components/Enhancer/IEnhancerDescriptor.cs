using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Components.Enhancer;

public interface IEnhancerDescriptor
{
    int Id { get; }
    string Name { get; }
    string? Description { get; }
    IEnhancerTargetDescriptor[] Targets { get; }
    int PropertyValueScope { get; }
    IEnhancerTargetDescriptor this[int target] { get; }
}