namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerDescriptors
{
    IEnhancerDescriptor[] Descriptors { get; }
    IEnhancerDescriptor? TryGet(int id);
    IEnhancerDescriptor this[int id] { get; }
}