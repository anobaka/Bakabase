using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using CsQuery;

namespace Bakabase.Service.Components.PostSources;

public sealed record SavedPostImage(string Id, string Url, string? Context, int Floor);
public sealed record SavedPostImageList(int Revision, IReadOnlyList<SavedPostImage> Images);

internal static class SavedPostImages
{
    // Snapshots may include thumbnails, lazy-loading attributes or a text link to the QR image.
    private static readonly string[] ImageAttributes = ["data-original-src", "data-original", "data-src", "src"];
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"};

    // The local resolver can return a synthetic address for domains behind the configured proxy.
    // Only that proxy may resolve such a name. Direct sockets still require public, pinned IPs.
    internal static async Task CheckProxyTargetAsync(Uri uri, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, ct); }
        catch (SocketException e) { throw new HttpRequestException("Image host could not be resolved.", e); }
        if (addresses.Length == 0 || addresses.Any(a => !PublicCloudHttp.IsPublicAddress(a) &&
            !(ProxyFakeAddresses.ProxyResolvesItself(uri.IdnHost) && ProxyFakeAddresses.Contains(a))))
            throw new HttpRequestException("Image target is outside the public network.");
    }

    internal static IReadOnlyList<SavedPostImage> Read(PostContent? snapshot)
    {
        if (snapshot == null || snapshot.Scope == "pastedText") return [];
        Uri.TryCreate(snapshot.SourceUrl, UriKind.Absolute, out var page);
        var images = new Dictionary<string, SavedPostImage>(StringComparer.Ordinal);
        Add(snapshot.MainHtml, 0);
        if (snapshot.Comments.Count > 0)
        {
            for (var i = 0; i < snapshot.Comments.Count; i++)
            {
                var comment = snapshot.Comments[i];
                Add(comment.Html, int.TryParse(comment.Floor, out var floor) ? floor : i + 1);
            }
        }
        else
        {
            for (var i = 0; i < snapshot.CommentHtmlList.Count; i++) Add(snapshot.CommentHtmlList[i], i + 1);
        }
        return images.Values.ToArray();

        void Add(string? html, int floor)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var document = CQ.CreateFragment(html);
            foreach (var node in document.Find("img, a[href]").Union(document.Filter("img, a[href]")))
            {
                var isImage = node.NodeName.Equals("img", StringComparison.OrdinalIgnoreCase);
                var raw = isImage ? ImageAttributes.Select(node.GetAttribute).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                    : node.GetAttribute("href");
                if (raw == null || !Uri.TryCreate(page, raw, out var uri) || uri.AbsoluteUri.Length > 2048) continue;
                try { PublicCloudHttp.EnsureSafeUri(uri); }
                catch (ArgumentException) { continue; }
                if (!isImage && !ImageExtensions.Contains(Path.GetExtension(uri.AbsolutePath))) continue;
                var url = uri.AbsoluteUri;
                if (images.ContainsKey(url)) continue;
                var label = node.GetAttribute("alt");
                if (string.IsNullOrWhiteSpace(label))
                {
                    var anchor = !isImage ? CQ.Create(node).Text().Trim() : null;
                    label = !string.IsNullOrWhiteSpace(anchor) && !Uri.TryCreate(anchor, UriKind.Absolute, out _) ? anchor : NearbyText(node);
                }
                var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
                label = string.IsNullOrWhiteSpace(label) ? null : Regex.Replace(label, @"\s+", " ").Trim();
                images.Add(url, new(id, url, label is {Length: > 120} ? label[..120] : label, floor));
            }
        }
    }

    private static string? NearbyText(IDomObject node)
    {
        var current = node.PreviousSibling ?? node.ParentNode?.PreviousSibling;
        for (var i = 0; i < 8 && current != null; i++, current = current.PreviousSibling)
        {
            var text = current.NodeType == NodeType.TEXT_NODE ? current.NodeValue : CQ.Create(current).Text();
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return null;
    }
}
