using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Bakabase.Remoting.Components.Forwarding;

/// <summary>
/// Single-use tickets that let a window arrive at a relay from another origin.
/// </summary>
/// <remarks>
/// <para>
/// A relay refuses browser requests that come from another site, because any page the
/// user has open could otherwise aim one at it and have it signed with this device's key.
/// Switching the window between servers is exactly such a request: a top-level navigation
/// from this device's own origin, or from another relay, to this one. The switcher asks
/// for a ticket first and carries it in the URL; the relay consumes it, and redirects to
/// the same URL without it, so everything after the first document is same-origin.
/// </para>
/// <para>
/// Bound to one relay's port, short-lived and consumed on first use, so a ticket that
/// leaks — into history, a log, a screenshot — opens nothing.
/// </para>
/// </remarks>
public sealed class RelayNavigationTokens(TimeProvider timeProvider)
{
    /// <summary>The query parameter a ticket travels in.</summary>
    public const string QueryName = "__bakabase_switch";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private const int MaxOutstanding = 64;

    private readonly ConcurrentDictionary<string, (int Port, DateTimeOffset Expires)> _tokens = new();

    public RelayNavigationTokens() : this(TimeProvider.System)
    {
    }

    public string Mint(int port)
    {
        Prune();

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _tokens[token] = (port, timeProvider.GetUtcNow() + Lifetime);

        return token;
    }

    /// <summary>Whether <paramref name="token"/> was minted for <paramref name="port"/> and is still fresh. Consumes it either way.</summary>
    public bool TryConsume(string? token, int port)
    {
        if (string.IsNullOrEmpty(token) || !_tokens.TryRemove(token, out var entry))
        {
            return false;
        }

        return entry.Port == port && entry.Expires > timeProvider.GetUtcNow();
    }

    private void Prune()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var (key, value) in _tokens)
        {
            if (value.Expires <= now)
            {
                _tokens.TryRemove(key, out _);
            }
        }

        // Nobody switches servers dozens of times a minute; a flood means something is
        // minting on a loop, and the oldest tickets are the ones nobody is waiting on.
        while (_tokens.Count >= MaxOutstanding)
        {
            var oldest = _tokens.OrderBy(t => t.Value.Expires).FirstOrDefault();

            if (oldest.Key == null || !_tokens.TryRemove(oldest.Key, out _))
            {
                break;
            }
        }
    }
}
