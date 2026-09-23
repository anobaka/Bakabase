using System.Collections.Immutable;

namespace Bakabase.Remoting.Components.UserMachine;

/// <param name="Template">The server's route template, e.g. <c>/resource/{id:int}/play</c>.</param>
public sealed record UserMachineRoute(string Method, string Template)
{
    /// <summary>
    /// Stable key, matching the server catalog's. Compare with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> — routes match without regard to
    /// case, and a controller written as <c>[Route("~/[controller]")]</c> yields the
    /// class name's casing rather than the lowercase form everyone writes.
    /// </summary>
    public string Key => $"{Method} {Template}";
}

/// <summary>
/// The endpoints whose effect lands on whatever machine runs them, and which the client
/// therefore has to run here rather than forward.
/// </summary>
/// <remarks>
/// <para>
/// A second copy of a list the server also holds, and deliberately so: the server
/// discovers it by reflecting over its own controllers, which this assembly cannot see.
/// What keeps the two honest is a test that holds them against each other, in an
/// assembly that references both.
/// </para>
/// <para>
/// Drift in either direction is a bug and they fail differently. A route missing here
/// gets forwarded, and the server refuses it — visible, but the feature is dead. A route
/// here that the server does not mark gets intercepted by a client that may have no
/// business handling it.
/// </para>
/// </remarks>
public static class UserMachineRoutes
{
    public static readonly ImmutableArray<UserMachineRoute> All =
    [
        new("GET", "/file/icon"),
        new("GET", "/file/recycle-bin"),
        new("GET", "/gui/url"),
        new("GET", "/player/playlist/{playlistId:int}/batch-play/candidates"),
        new("GET", "/resource/directory"),
        new("GET", "/resource/play/random"),
        new("GET", "/resource/{resourceId}/play"),
        new("GET", "/resource/{resourceId}/play-item"),
        new("GET", "/tampermonkey/install"),
        new("GET", "/tool/open"),
        new("GET", "/tool/open-file"),
        new("POST", "/aigc/artifacts/{id:int}/open"),
        new("POST", "/dlsite-work/{workId}/launch"),
        new("POST", "/player/batch-play"),
        new("POST", "/player/batch-play/candidates"),
        new("POST", "/player/playlist/{playlistId:int}/batch-play"),
        new("POST", "/tool/cookie-capture")
    ];

    private static readonly ImmutableHashSet<string> Keys =
        All.Select(r => r.Key).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string key) => Keys.Contains(key);

    /// <summary>
    /// The route this request would hit, or null if it is the server's to answer.
    /// </summary>
    public static UserMachineRouteMatch? Match(string method, string path)
    {
        foreach (var route in All)
        {
            if (!string.Equals(route.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = TryMatchTemplate(route.Template, path);
            if (values != null)
            {
                return new UserMachineRouteMatch(route, values);
            }
        }

        return null;
    }

    /// <summary>
    /// Matches one template, returning the captured segments or null.
    /// </summary>
    /// <remarks>
    /// Constraints are honoured rather than ignored. Without that, a request the server
    /// would route somewhere else entirely — <c>/resource/search/play</c> against
    /// <c>/resource/{id:int}/play</c> — would be intercepted here and answered by a
    /// handler that was never meant to see it.
    /// </remarks>
    private static Dictionary<string, string>? TryMatchTemplate(string template, string path)
    {
        var templateSegments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (templateSegments.Length != pathSegments.Length)
        {
            return null;
        }

        Dictionary<string, string>? values = null;

        for (var i = 0; i < templateSegments.Length; i++)
        {
            var templateSegment = templateSegments[i];
            var pathSegment = pathSegments[i];

            if (!templateSegment.StartsWith('{') || !templateSegment.EndsWith('}'))
            {
                if (!string.Equals(templateSegment, pathSegment, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                continue;
            }

            var inner = templateSegment[1..^1];
            var separator = inner.IndexOf(':');
            var name = separator < 0 ? inner : inner[..separator];
            var constraint = separator < 0 ? null : inner[(separator + 1)..];

            if (pathSegment.Length == 0)
            {
                return null;
            }

            if (string.Equals(constraint, "int", StringComparison.OrdinalIgnoreCase) &&
                !int.TryParse(pathSegment, out _))
            {
                return null;
            }

            values ??= [];
            values[name] = Uri.UnescapeDataString(pathSegment);
        }

        return values ?? [];
    }
}

public sealed record UserMachineRouteMatch(UserMachineRoute Route, IReadOnlyDictionary<string, string> Values);
