namespace Bakabase.Modules.Federation.Peers;

/// <summary>
/// What a node grant lets its holder read. A grant has exactly one scope, and grants of different
/// scopes live in separate collections, so a switch, a lease or a revocation of one scope never
/// touches the other. Every grant issued before data sync is <see cref="LibraryRead"/>.
/// Members are <c>const</c>: <see cref="Security.FederationEndpointAttribute.Scope"/> and the
/// <see cref="Security.NodePrincipal"/> default both need compile-time constants.
/// </summary>
public static class FederationScopes
{
    /// <summary>Browse, search and play this device's library (read-only).</summary>
    public const string LibraryRead = "library.read";

    /// <summary>Read this device's definitions through the data sync feed; never its library.</summary>
    public const string DataSyncRead = "datasync.read";

    /// <summary>Declared by an endpoint that a grant of either scope may reach (the handshake); never a grant's scope.</summary>
    public const string Any = "*";

    /// <summary>
    /// Whether a grant of <paramref name="grantScope"/> may reach an endpoint that declares
    /// <paramref name="endpointScope"/>. An endpoint that declares nothing admits nobody, so a new Export action
    /// without a scope fails closed.
    /// </summary>
    public static bool Admits(string? endpointScope, string? grantScope) =>
        grantScope is LibraryRead or DataSyncRead &&
        (endpointScope == Any || string.Equals(endpointScope, grantScope, StringComparison.Ordinal));
}
