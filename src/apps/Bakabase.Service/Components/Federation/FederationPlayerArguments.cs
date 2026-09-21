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
        var arguments = BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, target);
        // VLC keeps its native HTTP input: AVIO cannot pause ordinary HTTP streams in
        // VLC 3.x. FederationPlayerPolicy excludes VLC before launch when Darwin's
        // system proxy would receive this loopback ticket.
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
