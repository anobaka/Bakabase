namespace Bakabase.Modules.Acquisition.Abstractions.Components;

/// <summary>
/// Every registered step, indexed by kind. Two steps claiming the same kind is a wiring mistake and
/// fails at startup rather than silently letting one shadow the other.
/// </summary>
public interface IAcquisitionStepRegistry
{
    IReadOnlyList<IAcquisitionStep> All { get; }

    IAcquisitionStep? Get(string kind);

    bool TryGet(string kind, out IAcquisitionStep step);
}
