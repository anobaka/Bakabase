namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerDescriptors
{
    IEnhancerDescriptor[] Descriptors { get; }
    IEnhancerDescriptor? TryGet(int enhancerId);
    IEnhancerDescriptor this[int enhancerId] { get; }
}