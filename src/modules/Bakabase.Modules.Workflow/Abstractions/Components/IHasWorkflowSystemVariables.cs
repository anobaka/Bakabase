namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Domain system variables (capability map E4·"域系统变量"): stable facts an item can answer
/// about itself — a filesystem entry's extension or parent directory name — exposed to the
/// variable system without any capture step. Interpolation resolves the bag first, so a
/// captured variable can shadow a system one.
/// </summary>
public interface IHasWorkflowSystemVariables : IWorkflowItemContract
{
    /// <summary>Name → value. Recomputed on call so a transformed item answers with its
    /// current state.</summary>
    IReadOnlyDictionary<string, string> GetWorkflowSystemVariables();
}
