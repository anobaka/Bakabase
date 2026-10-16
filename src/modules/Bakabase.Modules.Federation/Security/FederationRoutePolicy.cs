using Bakabase.Modules.Federation.Peers;

namespace Bakabase.Modules.Federation.Security;

/// <summary>The explicit permission required by the remaining node protocol.</summary>
public enum FederationSharingRequirement { DataSync }

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
        return FederationSharingRequirement.DataSync;
    }

    public static bool ScopeMatches(FederationSharingRequirement requirement, string scope) =>
        requirement == FederationSharingRequirement.DataSync && scope == FederationScopes.DataSyncRead;

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
                   method == "POST" && segments.Length == 5 && Equal(segments[2], "pair") &&
                   Equal(segments[3], "datasync") && IsPairingStep(segments[4]);
        if (kind == FederationEndpointKind.Export)
        {
            if (segments.Length == 4 && Equal(segments[3], "handshake")) return method == "POST";
            if (segments.Length == 5 && Equal(segments[3], "datasync"))
                return method == "GET" &&
                       (Equal(segments[4], "head") || Equal(segments[4], "manifest") || Equal(segments[4], "changes"));
            return false;
        }
        if (segments.Length < 3) return false;
        if (Equal(segments[2], "management"))
            return segments.Length == 4 && (Equal(segments[3], "sources") || Equal(segments[3], "resource-display-options")) && method == "GET" ||
                   segments.Length == 4 && Equal(segments[3], "resource-display-options") && method == "PATCH" ||
                   segments.Length == 6 && NodeRequestSignature.IsIdentifier(segments[3]) && Equal(segments[4], "metadata") && Equal(segments[5], "control") && method == "POST" ||
                   segments.Length == 6 && NodeRequestSignature.IsIdentifier(segments[3]) && Equal(segments[4], "batch-play") && Equal(segments[5], "candidates") && method == "POST" ||
                   segments.Length == 5 && NodeRequestSignature.IsIdentifier(segments[3]) &&
                   ((Equal(segments[4], "request") || Equal(segments[4], "play") || Equal(segments[4], "batch-play") || Equal(segments[4], "directory") || Equal(segments[4], "upload")) && method == "POST" ||
                    (Equal(segments[4], "media") || Equal(segments[4], "cover") || Equal(segments[4], "asset")) && method is "GET" or "HEAD");
        if (Equal(segments[2], "servers"))
        {
            if (segments.Length == 3) return method == "GET";
            if (segments.Length == 4)
                return (Equal(segments[3], "probe") || Equal(segments[3], "pair")) && method == "POST" ||
                       Equal(segments[3], "discover") && method == "GET" ||
                       NodeRequestSignature.IsIdentifier(segments[3]) && method == "DELETE";
            if (segments.Length != 5) return false;
            if (Equal(segments[3], "requests"))
                return NodeRequestSignature.IsIdentifier(segments[4]) && method == "DELETE";
            return NodeRequestSignature.IsIdentifier(segments[3]) &&
                   (Equal(segments[4], "path-mapping-tree") && method == "GET" ||
                    Equal(segments[4], "path-mappings") && method == "PUT" ||
                    Equal(segments[4], "open") && method == "POST");
        }
        if (!Equal(segments[2], "peers")) return false;
        if (segments.Length == 3) return method == "GET";
        return segments.Length == 4 && Equal(segments[3], "name") && method == "PUT" ||
               segments.Length == 5 && Equal(segments[3], "identity") && Equal(segments[4], "reset") && method == "POST";
    }

    private static bool IsPairingStep(string segment) =>
        Equal(segment, "code") || Equal(segment, "request") || Equal(segment, "claim");

    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
