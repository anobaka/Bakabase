using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Components;

public class WorkflowActivityRegistry : IWorkflowActivityRegistry
{
    private readonly Dictionary<string, IWorkflowActivity> _byKind;

    public WorkflowActivityRegistry(IEnumerable<IWorkflowActivity> activities)
    {
        _byKind = activities.ToDictionary(a => a.Kind, StringComparer.Ordinal);
        All = _byKind.Values.ToList();
    }

    public IReadOnlyList<IWorkflowActivity> All { get; }

    public IWorkflowActivity? Get(string kind) =>
        _byKind.TryGetValue(kind, out var a) ? a : null;

    public bool TryGet(string kind, out IWorkflowActivity activity)
    {
        if (_byKind.TryGetValue(kind, out var a))
        {
            activity = a;
            return true;
        }
        activity = null!;
        return false;
    }
}
