namespace Bakabase.Modules.Federation.Security;

/// <summary>Only named protocol operations may cross the node boundary, including before MVC routing.</summary>
public static class FederationRoutePolicy
{
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
                   (Equal(segments[3], "code") || Equal(segments[3], "request") || Equal(segments[3], "claim"));
        if (kind == FederationEndpointKind.Export)
        {
            if (segments.Length == 4 && Equal(segments[3], "handshake")) return method == "POST";
            if (segments.Length == 4 && Equal(segments[3], "mapping-roots")) return method == "GET";
            if (segments.Length == 5 && Equal(segments[3], "resources") && Equal(segments[4], "resolve"))
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
            return segments.Length == 4 && Equal(segments[3], "resolve") && method == "POST";
        if (Equal(segments[2], "queries"))
            return segments.Length == 3 && method == "POST" ||
                   segments.Length == 4 && NodeRequestSignature.IsIdentifier(segments[3]) && method == "DELETE" ||
                   segments.Length == 5 && NodeRequestSignature.IsIdentifier(segments[3]) &&
                   Equal(segments[4], "pages") && method == "GET";
        if (!Equal(segments[2], "peers")) return false;
        if (segments.Length == 3) return method == "GET";
        if (segments.Length == 4)
            return Equal(segments[3], "sharing") && method == "PUT" ||
                   Equal(segments[3], "discover") && method == "GET" ||
                   (Equal(segments[3], "invite") || Equal(segments[3], "connect") || Equal(segments[3], "claim")) && method == "POST";
        if (segments.Length == 5)
        {
            if (Equal(segments[3], "grants")) return NodeRequestSignature.IsIdentifier(segments[4]) && method == "DELETE";
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

    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
