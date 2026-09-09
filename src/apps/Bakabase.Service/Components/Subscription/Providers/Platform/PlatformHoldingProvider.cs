using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Components;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Subscription.Providers.Platform;

/// <summary>
/// What the user holds on a platform, as a source.
/// <para>
/// A purchase list, an owned-games list and a favourites folder are all the same statement — "this
/// is mine" — and once a platform can be asked that, watching it is nothing more than asking on a
/// timer. Each of the three below is a name, a platform, and no configuration at all: the account
/// is already set up in that platform's own settings, and asking for it twice would be a second
/// place to get it wrong.
/// </para>
/// </summary>
public abstract class PlatformHoldingProvider(IServiceScopeFactory scopes) : ISubscriptionProvider
{
    public abstract string Kind { get; }
    public abstract string DisplayName { get; }

    /// <summary>
    /// Always. What these list is the user's own, so an item carries the platform's identity and
    /// can usually be fetched straight back from it.
    /// </summary>
    public SubscriptionSourceKind SourceKind => SubscriptionSourceKind.PlatformHolding;

    public abstract ResourceSource? ResourceSource { get; }

    /// <summary>
    /// Nothing to validate: the account lives in the platform's own settings, and a subscription
    /// that asked for it again would be a second place for it to be wrong.
    /// </summary>
    public Task<SubscriptionValidationResult> ValidateTargetAsync(string targetJson, CancellationToken ct) =>
        Task.FromResult(SubscriptionValidationResult.Valid);

    public string DescribeTarget(string targetJson) => DisplayName;

    public async Task<IReadOnlyList<SubscriptionItem>> FetchAllItemsAsync(SubscriptionRecord subscription,
        CancellationToken ct)
    {
        var source = ResourceSource!.Value;

        // A provider is a singleton and a connector reads the database, so the connector is
        // resolved per check rather than held: holding one would keep a scope alive forever.
        await using var scope = scopes.CreateAsyncScope();

        var connector = scope.ServiceProvider.GetRequiredService<IPlatformConnectorRegistry>().Get(source)
                        ?? throw new System.InvalidOperationException(
                            $"This build cannot speak for {source}.");

        var holdings = await connector.EnumerateHoldingsAsync(ct);

        return holdings
            .Select(h => new SubscriptionItem(h.SourceKey, h.DisplayName, null, h.CoverUrls,
                h.MetadataJson))
            .ToList();
    }
}

/// <summary>Everything bought on DLsite.</summary>
public class DLsitePurchasesProvider(IServiceScopeFactory scopes) : PlatformHoldingProvider(scopes)
{
    public override string Kind => "dlsite.purchases";
    public override string DisplayName => "DLsite Purchases";
    public override ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.DLsite;
}

/// <summary>Every game in the Steam library.</summary>
public class SteamOwnedGamesProvider(IServiceScopeFactory scopes) : PlatformHoldingProvider(scopes)
{
    public override string Kind => "steam.ownedGames";
    public override string DisplayName => "Steam Library";
    public override ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.Steam;
}

/// <summary>Every gallery in the ExHentai favourites.</summary>
public class ExHentaiFavoritesProvider(IServiceScopeFactory scopes) : PlatformHoldingProvider(scopes)
{
    public override string Kind => "exhentai.favorites";
    public override string DisplayName => "ExHentai Favorites";
    public override ResourceSource? ResourceSource => Bakabase.Abstractions.Models.Domain.Constants.ResourceSource.ExHentai;
}
