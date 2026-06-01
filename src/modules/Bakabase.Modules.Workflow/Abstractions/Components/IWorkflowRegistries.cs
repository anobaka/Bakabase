namespace Bakabase.Modules.Workflow.Abstractions.Components;

public interface IWorkflowTriggerRegistry
{
    IReadOnlyList<IWorkflowTrigger> All { get; }
    IWorkflowTrigger? Get(string kind);
    bool TryGet(string kind, out IWorkflowTrigger trigger);
}

public interface IWorkflowActivityRegistry
{
    IReadOnlyList<IWorkflowActivity> All { get; }
    IWorkflowActivity? Get(string kind);
    bool TryGet(string kind, out IWorkflowActivity activity);
}

public interface IWorkflowItemTypeRegistry
{
    IReadOnlyList<IWorkflowItemTypeDescriptor> All { get; }
    IWorkflowItemTypeDescriptor? Get(string itemType);
}
