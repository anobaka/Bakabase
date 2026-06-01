using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

public class WorkflowItemTypeRegistry : IWorkflowItemTypeRegistry
{
    private readonly Dictionary<string, IWorkflowItemTypeDescriptor> _byType;

    public WorkflowItemTypeRegistry(IEnumerable<IWorkflowItemTypeDescriptor> descriptors)
    {
        _byType = descriptors.ToDictionary(d => d.ItemType, StringComparer.Ordinal);
        All = _byType.Values.ToList();
    }

    public IReadOnlyList<IWorkflowItemTypeDescriptor> All { get; }

    public IWorkflowItemTypeDescriptor? Get(string itemType) =>
        _byType.TryGetValue(itemType, out var d) ? d : null;
}
