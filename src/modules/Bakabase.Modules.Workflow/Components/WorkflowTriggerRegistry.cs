using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

public class WorkflowTriggerRegistry : IWorkflowTriggerRegistry
{
    private readonly Dictionary<string, IWorkflowTrigger> _byKind;

    public WorkflowTriggerRegistry(IEnumerable<IWorkflowTrigger> triggers)
    {
        _byKind = triggers.ToDictionary(t => t.Kind, StringComparer.Ordinal);
        All = _byKind.Values.ToList();
    }

    public IReadOnlyList<IWorkflowTrigger> All { get; }

    public IWorkflowTrigger? Get(string kind) =>
        _byKind.TryGetValue(kind, out var t) ? t : null;

    public bool TryGet(string kind, out IWorkflowTrigger trigger)
    {
        if (_byKind.TryGetValue(kind, out var t))
        {
            trigger = t;
            return true;
        }
        trigger = null!;
        return false;
    }
}
