using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Text;

/// <summary>
/// Kind strings of the text family (docs/file-cleaning-workflow.html §3.4): transforms that
/// rewrite the working text of any <see cref="ITextWorkpiece"/> item. Deliberately its own
/// module token, not "fs" — the same nodes apply to any future text-bearing item type.
/// </summary>
public static class TextWorkflowKinds
{
    private const string Module = "text";

    /// <summary>Remove wrapped segments whose content matches a text set.</summary>
    public static readonly string TransformRemoveWrapped = WorkflowActivityKinds.Transform(Module, "removeWrapped");

    /// <summary>Remove bare occurrences matching a text set.</summary>
    public static readonly string TransformRemoveTexts = WorkflowActivityKinds.Transform(Module, "removeTexts");

    /// <summary>Clean up removal leftovers: repeated whitespace, edge whitespace, empty wrapper pairs.</summary>
    public static readonly string TransformTrim = WorkflowActivityKinds.Transform(Module, "trim");

    /// <summary>Capture named regex groups from the working text into the item's variable bag —
    /// the text is not changed (capability map E4).</summary>
    public static readonly string TransformCapture = WorkflowActivityKinds.Transform(Module, "capture");

    /// <summary>Rebuild the working text from a {var:…} template (capability map E4).</summary>
    public static readonly string TransformTemplate = WorkflowActivityKinds.Transform(Module, "template");
}
