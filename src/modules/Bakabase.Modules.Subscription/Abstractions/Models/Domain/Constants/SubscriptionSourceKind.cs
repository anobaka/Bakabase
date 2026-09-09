namespace Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;

/// <summary>
/// What relationship a source has to the things it lists. It decides how an item becomes a
/// resource, which is the one thing the framework has to know about a provider.
/// </summary>
public enum SubscriptionSourceKind
{
    /// <summary>
    /// The user holds these on a platform — a purchase list, an owned-games list, a favourites
    /// folder. Items carry a platform identity and can usually be fetched from it.
    /// </summary>
    PlatformHolding = 1,

    /// <summary>
    /// An authority on what exists — a circle's works, a series' entries. Items carry an identity
    /// but say nothing about where to get them.
    /// </summary>
    Catalog = 2,

    /// <summary>
    /// Somewhere people share links — a forum board, a feed. An item is one act of sharing rather
    /// than one work, so it never carries an identity: only a lead.
    /// </summary>
    SharingChannel = 3,
}
