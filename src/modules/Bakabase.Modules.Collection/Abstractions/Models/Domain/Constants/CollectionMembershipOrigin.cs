namespace Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;

/// <summary>
/// How a resource came to be in a collection. Only the ways that are written down: a resource that
/// is in a collection because it matches its rule has no row at all, and so no origin.
/// </summary>
public enum CollectionMembershipOrigin
{
    /// <summary>Someone put it there.</summary>
    Manual = 1,

    /// <summary>A subscription brought it in.</summary>
    Subscription = 2
}
