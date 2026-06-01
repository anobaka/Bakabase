using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Input;

namespace Bakabase.Modules.Subscription.Abstractions.Services;

public interface ISubscriptionService
{
    Task<SubscriptionRecord> CreateAsync(SubscriptionCreationInputModel input, CancellationToken ct = default);

    Task<SubscriptionRecord> UpdateAsync(int id, SubscriptionUpdateInputModel input, CancellationToken ct = default);

    Task DeleteAsync(int id);

    Task<SubscriptionRecord?> GetAsync(int id);

    Task<List<SubscriptionRecord>> SearchAsync(SubscriptionSearchInputModel input);

    /// <summary>
    /// Run a check for one subscription. Looks up the provider, loads the last snapshot,
    /// diffs, persists the new snapshot, and emits a notification when new items appear.
    /// Returns a summary (counts + first-run flag + error) the UI can render directly;
    /// null when the subscription doesn't exist.
    /// </summary>
    Task<SubscriptionCheckSummary?> RunCheckAsync(int id, CancellationToken ct = default);
}
