namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// The text-family contract (capability map E3): an item exposing one piece of working text the
/// <c>transform.text.*</c> activities may rewrite. For a filesystem entry the working text is its
/// working name; a future gallery item can expose its title the same way and the whole text
/// family applies to it unchanged.
/// </summary>
public interface ITextWorkpiece : IWorkflowItemContract
{
    /// <summary>The text being worked on.</summary>
    string WorkingText { get; }

    /// <summary>
    /// A copy of this item (same CLR type, same item-type tag) with the working text replaced —
    /// text transforms are Passthrough by construction, so the copy must not change what the
    /// item is, only what it says.
    /// </summary>
    object WithWorkingText(string workingText);
}
