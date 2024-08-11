namespace Bakabase.Modules.Enhancer.Abstractions;

public interface IEnhancerDescriptor
{
    int Id { get; }
    string Name { get; }
    string? Description { get; }
    IEnhancerTargetDescriptor[] Targets { get; }
    int PropertyValueScope { get; }
    IEnhancerTargetDescriptor this[int target] { get; }
}