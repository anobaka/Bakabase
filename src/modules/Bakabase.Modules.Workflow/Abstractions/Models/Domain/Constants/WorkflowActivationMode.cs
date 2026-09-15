namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

/// <summary>The normal source of runs, independent of whether a trigger supports manual replay.</summary>
public enum WorkflowActivationMode
{
    Unknown = 0,
    Manual = 1,
    Module = 2,
    SystemEvent = 3,
    Schedule = 4,
    Watch = 5,
}
