using Bakabase.Modules.Federation.Peers;

namespace Bakabase.Modules.Federation.Security;

/// <summary>
/// Which sharing switch a node route needs before anything else is looked at (§7.3). Library sharing and
/// definitions sharing are separate switches, so a route never answers because the other one is on.
/// </summary>
public enum FederationSharingRequirement
{
    /// <summary>Library sharing (<c>SharingEnabled</c>).</summary>
    Library,

    /// <summary>Definitions sharing (<c>DataSyncSharingEnabled</c>).</summary>
    DataSync,

    /// <summary>Either switch: <c>info</c>, which a device sharing only its definitions must still answer.</summary>
    Either,

    /// <summary>
    /// Either switch before authentication, then the switch of the grant's own scope: the handshake, which a grant
    /// of either scope makes.
    /// </summary>
    GrantScope
}

/// <summary>Only named protocol operations may cross the node boundary, including before MVC routing.</summary>
public static class FederationRoutePolicy
{
    /// <summary>
    /// The switch a Public or Export route needs (§7.3), for a route <see cref="Allows"/> accepts; null for anything
    /// else, which the caller refuses.
    /// </summary>
    public static FederationSharingRequirement? RequiredSharing(FederationEndpointKind kind, string method, string path)
    {
        if (kind == FederationEndpointKind.Local || !Allows(kind, method, path)) return null;
        var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (kind == FederationEndpointKind.Public)
            return segments.Length == 3 ? FederationSharingRequirement.Either
                : segments.Length == 5 ? FederationSharingRequirement.DataSync
                : FederationSharingRequirement.Library;
        if (segments.Length == 4 && Equal(segments[3], "handshake")) return FederationSharingRequirement.GrantScope;
        return segments.Length == 5 && Equal(segments[3], "datasync")
            ? FederationSharingRequirement.DataSync
            : FederationSharingRequirement.Library;
    }

    /// <summary>
    /// The grant scope a principal must hold for a route's requirement, after authentication: the library's for
    /// library routes, <c>datasync.read</c> for the feed, and either for the handshake (whose grant's own switch
    /// was checked while authenticating).
    /// </summary>
    public static bool ScopeMatches(FederationSharingRequirement requirement, string scope) => requirement switch
    {
        FederationSharingRequirement.Library => scope == FederationScopes.LibraryRead,
        FederationSharingRequirement.DataSync => scope == FederationScopes.DataSyncRead,
        FederationSharingRequirement.GrantScope => scope is FederationScopes.LibraryRead or FederationScopes.DataSyncRead,
        _ => false
    };

    public static FederationEndpointKind? Classify(string path)
    {
        if (path.Equals("/federation/local", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/federation/local/", StringComparison.OrdinalIgnoreCase)) return FederationEndpointKind.Local;
        if (path.StartsWith("/federation/v1/export/", StringComparison.OrdinalIgnoreCase)) return FederationEndpointKind.Export;
        if (path.Equals("/federation/v1/info", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/federation/v1/pair/", StringComparison.OrdinalIgnoreCase)) return FederationEndpointKind.Public;
        return null;
    }

    public static bool IsFederationPath(string path) =>
        path.Equals("/federation", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/federation/", StringComparison.OrdinalIgnoreCase);

    public static bool Allows(FederationEndpointKind kind, string method, string path)
    {
        var segments = path.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        method = method.ToUpperInvariant();
        if (kind == FederationEndpointKind.Public)
            return method == "GET" && path.Equals("/federation/v1/info", StringComparison.OrdinalIgnoreCase) ||
                   method == "POST" && segments.Length == 4 && Equal(segments[2], "pair") &&
                   IsPairingStep(segments[3]) ||
                   method == "POST" && segments.Length == 5 && Equal(segments[2], "pair") &&
                   Equal(segments[3], "datasync") && IsPairingStep(segments[4]);
        if (kind == FederationEndpointKind.Export)
        {
            if (segments.Length == 4 && Equal(segments[3], "handshake")) return method == "POST";
            if (segments.Length == 5 && Equal(segments[3], "datasync"))
                return method == "GET" &&
                       (Equal(segments[4], "head") || Equal(segments[4], "manifest") || Equal(segments[4], "changes"));
            if (segments.Length == 4 && Equal(segments[3], "mapping-roots")) return method == "GET";
            if (segments.Length == 5 && Equal(segments[3], "resources") &&
                (Equal(segments[4], "resolve") || Equal(segments[4], "location")))
                return method == "POST";
            if (segments.Length == 5 && Equal(segments[3], "assets") && NodeRequestSignature.IsIdentifier(segments[4]))
                return method is "GET" or "HEAD";
            if (segments.Length >= 4 && Equal(segments[3], "queries"))
                return segments.Length == 4 && method == "POST" ||
                       segments.Length == 5 && NodeRequestSignature.IsIdentifier(segments[4]) && method == "DELETE" ||
                       segments.Length == 6 && NodeRequestSignature.IsIdentifier(segments[4]) &&
                       (Equal(segments[5], "pages") && method == "GET" || Equal(segments[5], "validate") && method == "POST");
            return false;
        }
        if (segments.Length < 3) return false;
        if (Equal(segments[2], "media"))
            return segments.Length == 4 && NodeRequestSignature.IsIdentifier(segments[3]) && method is "GET" or "HEAD";
        if (Equal(segments[2], "playback-sessions")) return segments.Length == 3 && method == "POST";
        if (Equal(segments[2], "resources"))
            return segments.Length == 4 && (Equal(segments[3], "resolve") || Equal(segments[3], "open-directory")) && method == "POST";
        if (Equal(segments[2], "queries"))
            return segments.Length == 3 && method == "POST" ||
                   segments.Length == 4 && NodeRequestSignature.IsIdentifier(segments[3]) && method == "DELETE" ||
                   segments.Length == 5 && NodeRequestSignature.IsIdentifier(segments[3]) &&
                   Equal(segments[4], "pages") && method == "GET";
        if (Equal(segments[2], "servers"))
        {
            if (segments.Length == 3) return method == "GET";
            if (segments.Length == 4)
                return (Equal(segments[3], "probe") || Equal(segments[3], "pair") ||
                        Equal(segments[3], "import-legacy-client")) && method == "POST" ||
                       Equal(segments[3], "discover") && method == "GET" ||
                       NodeRequestSignature.IsIdentifier(segments[3]) && method == "DELETE";
            if (segments.Length != 5) return false;
            if (Equal(segments[3], "requests"))
                return NodeRequestSignature.IsIdentifier(segments[4]) && method == "DELETE";
            return NodeRequestSignature.IsIdentifier(segments[3]) &&
                   (Equal(segments[4], "path-mappings") && method == "PUT" ||
                    Equal(segments[4], "open") && method == "POST");
        }
        if (!Equal(segments[2], "peers")) return false;
        if (segments.Length == 3) return method == "GET";
        if (segments.Length == 4)
            return (Equal(segments[3], "sharing") || Equal(segments[3], "browsing") || Equal(segments[3], "name")) &&
                   method == "PUT" ||
                   NodeRequestSignature.IsIdentifier(segments[3]) && method == "DELETE" ||
                   Equal(segments[3], "discover") && method == "GET" ||
                   (Equal(segments[3], "invite") || Equal(segments[3], "connect") || Equal(segments[3], "claim")) && method == "POST";
        if (segments.Length == 5)
        {
            if (Equal(segments[3], "grants") || Equal(segments[3], "requests"))
                return NodeRequestSignature.IsIdentifier(segments[4]) && method == "DELETE";
            if (Equal(segments[3], "identity") && Equal(segments[4], "reset")) return method == "POST";
            if (!NodeRequestSignature.IsIdentifier(segments[3])) return false;
            return Equal(segments[4], "outbound") && method == "DELETE" ||
                   (Equal(segments[4], "enabled") || Equal(segments[4], "path-mappings")) && method == "PUT" ||
                   Equal(segments[4], "mapping-roots") && method == "GET";
        }
        return segments.Length == 6 && Equal(segments[3], "requests") &&
               NodeRequestSignature.IsIdentifier(segments[4]) &&
               (Equal(segments[5], "approve") || Equal(segments[5], "reject")) && method == "POST";
    }

    private static bool IsPairingStep(string segment) =>
        Equal(segment, "code") || Equal(segment, "request") || Equal(segment, "claim");

    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
