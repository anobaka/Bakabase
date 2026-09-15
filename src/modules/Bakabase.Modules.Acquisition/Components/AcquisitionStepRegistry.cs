using Bakabase.Modules.Acquisition.Abstractions.Components;

namespace Bakabase.Modules.Acquisition.Components;

public class AcquisitionStepRegistry : IAcquisitionStepRegistry
{
    private readonly Dictionary<string, IAcquisitionStep> _byKind;

    public AcquisitionStepRegistry(IEnumerable<IAcquisitionStep> steps)
    {
        _byKind = new Dictionary<string, IAcquisitionStep>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (!_byKind.TryAdd(step.Kind, step))
            {
                // Failing here beats one step silently shadowing another and a recipe quietly
                // doing something other than what it says.
                throw new InvalidOperationException(
                    $"Two acquisition steps claim the kind '{step.Kind}': " +
                    $"{_byKind[step.Kind].GetType().FullName} and {step.GetType().FullName}.");
            }
        }

        All = _byKind.Values.ToList();
    }

    public IReadOnlyList<IAcquisitionStep> All { get; }

    public IAcquisitionStep? Get(string kind) => _byKind.GetValueOrDefault(kind);

    public bool TryGet(string kind, out IAcquisitionStep step)
    {
        if (_byKind.TryGetValue(kind, out var s))
        {
            step = s;

            return true;
        }

        step = null!;

        return false;
    }
}
