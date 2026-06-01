using Bakabase.Modules.Subscription.Abstractions.Components;

namespace Bakabase.Modules.Subscription.Components;

public class SubscriptionProviderRegistry : ISubscriptionProviderRegistry
{
    private readonly Dictionary<string, ISubscriptionProvider> _byKind;

    public SubscriptionProviderRegistry(IEnumerable<ISubscriptionProvider> providers)
    {
        _byKind = providers.ToDictionary(p => p.Kind, StringComparer.OrdinalIgnoreCase);
        All = _byKind.Values.ToList();
    }

    public IReadOnlyList<ISubscriptionProvider> All { get; }

    public ISubscriptionProvider? Get(string kind) =>
        _byKind.TryGetValue(kind, out var p) ? p : null;

    public bool TryGet(string kind, out ISubscriptionProvider provider)
    {
        if (_byKind.TryGetValue(kind, out var p))
        {
            provider = p;
            return true;
        }
        provider = null!;
        return false;
    }
}
