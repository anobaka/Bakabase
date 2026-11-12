using System.Text.RegularExpressions;

namespace Bakabase.Modules.Downloader.Cloud;

/// <summary>Public share and optional selected folder/file path, as encoded by PikPak's web client.
/// A selected node is not an access token and must still belong to the listed share.</summary>
public sealed record PikPakShareLocator(string ShareId, IReadOnlyList<string> NodeIds)
{
    public static bool TryParse(string url, out PikPakShareLocator? result)
    {
        result = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.IdnHost is not ("mypikpak.com" or "www.mypikpak.com" or "pikpak.com")) return false;
        if (uri.AbsolutePath.EndsWith("//", StringComparison.Ordinal)) return false;
        var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (segments.Length is < 3 or > 35 || segments[1] != "s" ||
            segments.Skip(2).Any(value => value.Length is 0 or > 128 || !Regex.IsMatch(value, @"^[A-Za-z0-9_-]+$"))) return false;
        var share = segments[2];
        var nodes = new List<string>();
        try
        {
            foreach (var segment in segments.Skip(3))
            {
                // Current official client (2026-10-22): node IDs of the same length are raw;
                // otherwise XOR their base64url payload with the share payload, retaining the
                // two-character suffix. The last three characters verify the decoded prefix.
                if (segment.Length == share.Length) { nodes.Add(segment); continue; }
                if (share.Length < 5 || segment.Length != share.Length + 4 || segment[^4] != '_') return false;
                var encoded = Decode(segment[..^6]);
                var key = Decode(share[..^2]);
                if (encoded.Length != key.Length) return false;
                for (var i = 0; i < encoded.Length; i++) encoded[i] ^= key[i];
                var node = Encode(encoded) + segment[^6..^4];
                if (!node.StartsWith(segment[^3..], StringComparison.Ordinal)) return false;
                nodes.Add(node);
            }
        }
        catch (FormatException) { return false; }
        if (nodes.Distinct(StringComparer.Ordinal).Count() != nodes.Count) return false;
        result = new(share, nodes);
        return true;
    }

    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') +
        new string('=', (4 - value.Length % 4) % 4));
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
