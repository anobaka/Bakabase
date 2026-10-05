using Bakabase.Abstractions.Components.Tasks;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>Source-owned scheduling and restart behavior shared by every workflow entry point.</summary>
public interface IWorkflowRunSchedulingPolicy
{
    bool AppliesTo(string triggerKind);
    bool ResumeOnStartup { get; }
    BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder);
}
