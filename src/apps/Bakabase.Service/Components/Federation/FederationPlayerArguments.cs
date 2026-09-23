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
        // mpv's newer curl backend interprets direct:// as a proxy host. Select
        // its long-supported FFmpeg input explicitly for this validated ticket.
        // lavf:// changes mpv's origin classification, so disable references below:
        // the exported asset represents one file, never a playlist or nested I/O.
        var target = known == KnownPlayerDefinitions.Mpv ? "lavf://" + uri.AbsoluteUri : uri.AbsoluteUri;
        var arguments = BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, target);
        // VLC keeps its native HTTP input: AVIO cannot pause ordinary HTTP streams in
        // VLC 3.x. FederationPlayerPolicy excludes VLC before launch when Darwin's
        // system proxy would receive this loopback ticket.
        // The selected mpv input passes nonempty http-proxy to libavformat, which
        // ignores non-HTTP values. Braces limit the override to this file. IINA's supported CLI
        // applies --mpv-* to a new PlayerCore; its override lasts for that playback
        // instance (including files later opened there), without writing preferences.
        // Use a nonempty value: empty mpv options also fall back to environment proxies.
        if (known == KnownPlayerDefinitions.Mpv)
            return "--{ --http-proxy=direct:// " + arguments + " --access-references=no --}";
        if (known == KnownPlayerDefinitions.Iina)
            return "--mpv-http-proxy=direct:// " + arguments;
        return arguments;
    }
}
