using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

public static class NotificationWorkflowActivityKinds
{
    private const string Module = "notification";

    /// <summary>Generic: write a persistent notification with templated title + body.</summary>
    public static readonly string Create = WorkflowActivityKinds.Action(Module, "create");
}
