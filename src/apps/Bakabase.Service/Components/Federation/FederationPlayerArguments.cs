using System;
using System.Linq;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Service.Components.Federation;

/// <summary>Player arguments for a validated local file or a media ticket issued by this host.</summary>
public static class FederationPlayerArguments
{
    public static string Build(ResolvedPlayer player, string? localPath, string mediaUrl)
    {
        if (localPath != null)
            return BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, localPath);

        const string prefix = "/federation/local/media/";
        if (mediaUrl.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '%' or '\\') ||
            !Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !uri.IsLoopback ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) ||
            uri.AbsolutePath.Length != prefix.Length + 64 ||
            !uri.AbsolutePath[prefix.Length..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Expected a loopback federation media ticket URL.", nameof(mediaUrl));

        var known = KnownPlayerDefinitions.MatchByExecutable(player.ExecutablePath);
        var target = uri.AbsoluteUri;
        if (known == KnownPlayerDefinitions.Vlc)
        {
            // VLC 3.x on macOS reads the system HTTP proxy without checking the destination
            // (src/darwin/netconf.c), so neither localhost nor no_proxy bypasses it. Its AVIO
            // input uses libavformat instead and retains Range/seek support. An explicitly
            // non-HTTP proxy value disables libavformat's proxy lookup; an empty value is
            // discarded by VLC's option parser and would inherit http_proxy again.
            // The colon option belongs only to this input, not the user's VLC preferences.
            return BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, "avio://" + target) +
                   " :avio-options={http_proxy=direct://}";
        }

        var arguments = BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, target);
        // mpv passes nonempty http-proxy through to libavformat, which ignores non-HTTP
        // values. Its braces limit the override to this file. IINA's supported CLI
        // applies --mpv-* to a new PlayerCore; its override lasts for that playback
        // instance (including files later opened there), without writing preferences.
        // Use a nonempty value: empty mpv options also fall back to environment proxies.
        if (known == KnownPlayerDefinitions.Mpv)
            return "--{ --http-proxy=direct:// " + arguments + " --}";
        if (known == KnownPlayerDefinitions.Iina)
            return "--mpv-http-proxy=direct:// " + arguments;
        return arguments;
    }
}
