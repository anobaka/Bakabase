using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

public record WorkflowRunResumeInputModel
{
    /// <summary>
    /// The answer to whatever the run is waiting for, shaped by the activity that suspended — it
    /// is the only party that knows how to read it, so the engine carries it as opaque text.
    /// </summary>
    [Required]
    public string SignalJson { get; set; } = "{}";
}
