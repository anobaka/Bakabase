namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>
/// Declares how an activity consumes source-independent file instructions. Intake preflight
/// inspects this capability, rather than maintaining a list of workflow kinds or sources.
/// Implementations must only read configuration; no filesystem or network operations.
/// </summary>
public interface IFileProcessingPlanConsumer
{
    string GetArchivePolicy(string configJson);
}
