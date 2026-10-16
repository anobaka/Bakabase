namespace Bakabase.Modules.Federation.Peers;

/// <summary>Direct node grants only authorize definitions synchronization.</summary>
public static class FederationScopes
{
    public const string DataSyncRead = "datasync.read";
    /// <summary>The identity handshake accepts any supported node grant.</summary>
    public const string Any = "*";
    public static bool Admits(string? endpointScope, string? grantScope) =>
        grantScope == DataSyncRead && (endpointScope == Any || endpointScope == grantScope);
}
