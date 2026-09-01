namespace Bakabase.Modules.Workflow.Abstractions.Components;

/// <summary>
/// Marker for workflow capability contracts (capability map E3). A contract is an interface an
/// item's CLR type implements so whole families of activities apply to it — an activity declares
/// <see cref="IWorkflowActivity.AcceptedItemInterface"/> once, an item type implements the
/// contract once, and no per-pair accept list ever has to grow. Deriving from this marker is what
/// makes a contract discoverable: descriptors report exactly the implemented interfaces that
/// derive from it, so unrelated CLR plumbing (IEquatable and friends) never leaks into the
/// editor's compatibility metadata.
/// </summary>
public interface IWorkflowItemContract;
