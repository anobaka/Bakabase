namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// An item that knows which resource it is about.
/// <para>
/// The capability an activity needs in order to act on a resource without caring where the item
/// came from. It lives here rather than with any one producer so that a module which emits such
/// items and a module which consumes them never have to know about each other.
/// </para>
/// </summary>
public interface IHasResourceId : IWorkflowItemContract
{
    int ResourceId { get; }
}
