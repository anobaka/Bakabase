using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Fs;

/// <summary>
/// Kind strings owned by the filesystem workflow domain (the file-cleaning vertical,
/// docs/file-cleaning-workflow.html).
/// </summary>
public static class FsWorkflowKinds
{
    private const string Module = "fs";

    /// <summary>User-initiated scan over configured roots — the first manual-payload-free
    /// trigger (its parameters live on the definition, not in an event).</summary>
    public static readonly string TriggerManualScan = WorkflowTriggerKinds.Build(Module, "manualScan");

    public static readonly string TransformFileNameOp = WorkflowActivityKinds.Transform(Module, "fileNameOp");

    /// <summary>The design's "expand.fs.children" — expansion is a Transform in the kind
    /// grammar (category tokens are filter/action/transform only).</summary>
    public static readonly string TransformExpandChildren = WorkflowActivityKinds.Transform(Module, "expandChildren");

    public static readonly string ActionSaveName = WorkflowActivityKinds.Action(Module, "saveName");
}
