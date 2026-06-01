namespace Bakabase.Modules.Subscription.Abstractions.Components;

/// <summary>Lookup providers registered via DI by their <see cref="ISubscriptionProvider.Kind"/>.</summary>
public interface ISubscriptionProviderRegistry
{
    IReadOnlyList<ISubscriptionProvider> All { get; }

    ISubscriptionProvider? Get(string kind);

    bool TryGet(string kind, out ISubscriptionProvider provider);
}
