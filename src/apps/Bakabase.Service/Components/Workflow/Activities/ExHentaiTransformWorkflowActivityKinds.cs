using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities;

public static class ExHentaiTransformWorkflowActivityKinds
{
    private const string Module = "exhentai";

    /// <summary>Deterministic: run a search query and emit the first gallery hit.</summary>
    public static readonly string QueryToGallery =
        WorkflowActivityKinds.Transform(Module, "queryToGallery");
}

public static class AiWorkflowActivityKinds
{
    private const string Module = "ai";

    /// <summary>Generic LLM bridge: takes any item type, outputs the type the next step needs (or the user-pinned target).</summary>
    public static readonly string Transform = WorkflowActivityKinds.Transform(Module, "transform");
}
