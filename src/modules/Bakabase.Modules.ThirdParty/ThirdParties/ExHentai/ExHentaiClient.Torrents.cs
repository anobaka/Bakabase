using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public partial class ExHentaiClient
{
    private const int MaxTorrentResponseBytes = 4 * 1024 * 1024;
    private const string PersonalizedTorrentPathPattern =
        "^/torrent/(?<tracker>[0-9]+)/[0-9]+-[a-zA-Z0-9]+/(?<hash>[a-fA-F0-9]{40})\\.torrent$";

    private static string ResolveTorrentDownloadLink(string torrentPageUrl, string href, string? onclick)
    {
        var page = new Uri(torrentPageUrl);
        var fallback = new Uri(page, WebUtility.HtmlDecode(href));
        if (string.IsNullOrWhiteSpace(onclick)) return fallback.AbsoluteUri;

        // The site displays a public href but clicks navigate to a personalized torrent.
        // Read only this literal assignment; never evaluate scripts or accept other routes.
        var assignment = Regex.Match(WebUtility.HtmlDecode(onclick),
            "^\\s*document\\.location\\s*=\\s*(?<quote>['\"])(?<url>[^'\"\\r\\n]+)\\k<quote>\\s*;\\s*return\\s+false\\s*;?\\s*$",
            RegexOptions.CultureInvariant);
        if (!assignment.Success ||
            !Uri.TryCreate(assignment.Groups["url"].Value, UriKind.Absolute, out var personalized) ||
            personalized.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(personalized.Authority, page.Authority, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(personalized.UserInfo) ||
            !string.IsNullOrEmpty(personalized.Query) || !string.IsNullOrEmpty(personalized.Fragment))
            return fallback.AbsoluteUri;

        var publicPath = Regex.Match(fallback.AbsolutePath,
            "^/torrent/(?<tracker>[0-9]+)/(?<hash>[a-fA-F0-9]{40})\\.torrent$", RegexOptions.CultureInvariant);
        var accountPath = Regex.Match(personalized.AbsolutePath, PersonalizedTorrentPathPattern,
            RegexOptions.CultureInvariant);
        return publicPath.Success && accountPath.Success &&
               string.Equals(fallback.Authority, page.Authority, StringComparison.OrdinalIgnoreCase) &&
               fallback.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(fallback.UserInfo) &&
               publicPath.Groups["tracker"].Value == accountPath.Groups["tracker"].Value &&
               string.Equals(publicPath.Groups["hash"].Value, accountPath.Groups["hash"].Value,
                   StringComparison.OrdinalIgnoreCase)
            ? personalized.AbsoluteUri
            : fallback.AbsoluteUri;
    }

    public async Task DownloadTorrent(string torrentUrl, string downloadPath, CancellationToken ct = default,
        ExHentaiRequestContext? context = null)
    {
        var uri = new Uri(torrentUrl, UriKind.Absolute);
        byte[] bytes;
        for (var redirects = 0;; redirects++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                throw new InvalidDataException("Torrent downloads require an HTTP(S) link.");
            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("Torrent download links must not contain URL credentials.");
            if (uri.LocalPath.StartsWith("/fullimg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A torrent link points to an original-image download. It was blocked to avoid account charges.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var trusted = uri.Host is "exhentai.org" or "e-hentai.org" or "www.exhentai.org" or "www.e-hentai.org";
            if (trusted && uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Account requests require HTTPS.");
            var personalizedPath = Regex.Match(uri.AbsolutePath, PersonalizedTorrentPathPattern,
                RegexOptions.CultureInvariant);
            if (!trusted && personalizedPath.Success)
                throw new InvalidDataException("A personalized torrent link cannot be sent to another site.");
            if (personalizedPath.Success)
                request.Options.Set(ThirdPartyRequestOptions.RequestLogKey,
                    $"{uri.Scheme}://{uri.Authority}/torrent/{personalizedPath.Groups["tracker"].Value}/[redacted]/{personalizedPath.Groups["hash"].Value}.torrent");
            // A normal torrent download continues the authenticated gallery/window session,
            // including cookies refreshed by Set-Cookie. A new "torrent:host" container lost
            // that state. Only explicit account snapshots bypass configured account headers.
            request.Options.Set(ThirdPartyRequestOptions.SkipConfiguredHeaders, !trusted || context != null);
            request.Options.Set(ThirdPartyRequestOptions.AccountKey,
                trusted && context == null ? "default" : $"{context?.AccountKey ?? "torrent"}:{uri.Host}");
            if (trusted && context != null)
            {
                request.Options.Set(ThirdPartyRequestOptions.Cookie, context.Cookie);
                request.Headers.TryAddWithoutValidation("Cookie", context.Cookie);
            }
            else if (!trusted)
            {
                request.Options.Set(ThirdPartyRequestOptions.Cookie, "");
                request.Options.Set(ThirdPartyRequestOptions.SuppressSensitiveHeaders, true);
            }

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                if (redirects >= 5 || response.Headers.Location == null)
                    throw new InvalidDataException("The torrent download redirect is missing or loops. Choose another torrent link.");
                uri = new Uri(uri, response.Headers.Location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxTorrentResponseBytes)
                throw new InvalidDataException("The torrent metadata exceeds 4 MiB.");
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                if (output.Length + read > MaxTorrentResponseBytes)
                    throw new InvalidDataException("The torrent metadata exceeds 4 MiB.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            bytes = output.ToArray();
            if (bytes.Length == 0)
                throw new InvalidDataException("The torrent download returned an empty response. Check your login and the torrent link.");
            var prefix = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 256)))
                .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (prefix.StartsWith('<') ||
                (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true &&
                 bytes[0] != (byte) 'd'))
                throw new InvalidDataException("The torrent download returned an HTML error or login page instead of a torrent. " +
                                               "Check your login or choose another torrent link.");
            if (!IsBencodedTorrentResponse(bytes))
                throw new InvalidDataException($"The torrent download did not return valid BitTorrent metadata ({bytes.Length} bytes). " +
                                               "The link may be expired or unavailable.");
            break;
        }

        // An existing filename is not evidence of a completed download: older versions could
        // save an HTML error page there. Always obtain a fresh response and replace atomically.
        var temporary = downloadPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, downloadPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    // A transport-level check, without a dependency on the torrent engine. The producer performs
    // full MonoTorrent and path validation before promoting the file or recording its result.
    private static bool IsBencodedTorrentResponse(byte[] bytes)
    {
        var position = 0;
        var hasInfo = false;
        try
        {
            if (bytes[0] != (byte) 'd') return false;
            ReadValue(0);
            return hasInfo && position == bytes.Length;
        }
        catch (InvalidDataException)
        {
            return false;
        }

        (int Offset, int Length) ReadString()
        {
            var length = 0;
            var start = position;
            while (position < bytes.Length && bytes[position] is >= (byte) '0' and <= (byte) '9')
            {
                var digit = bytes[position++] - '0';
                if (length > (bytes.Length - digit) / 10) throw new InvalidDataException();
                length = length * 10 + digit;
            }
            if (position == start || position >= bytes.Length || bytes[position++] != (byte) ':' ||
                length > bytes.Length - position) throw new InvalidDataException();
            var offset = position;
            position += length;
            return (offset, length);
        }

        void ReadValue(int depth)
        {
            if (depth > 64 || position >= bytes.Length) throw new InvalidDataException();
            var kind = bytes[position];
            if (kind is >= (byte) '0' and <= (byte) '9')
            {
                ReadString();
                return;
            }
            position++;
            if (kind == (byte) 'i')
            {
                if (position < bytes.Length && bytes[position] == (byte) '-') position++;
                var start = position;
                while (position < bytes.Length && bytes[position] is >= (byte) '0' and <= (byte) '9') position++;
                if (position == start || position >= bytes.Length || bytes[position++] != (byte) 'e')
                    throw new InvalidDataException();
                return;
            }
            if (kind != (byte) 'd' && kind != (byte) 'l') throw new InvalidDataException();
            while (position < bytes.Length && bytes[position] != (byte) 'e')
            {
                if (kind == (byte) 'd')
                {
                    var key = ReadString();
                    if (depth == 0 && bytes.AsSpan(key.Offset, key.Length).SequenceEqual("info"u8))
                    {
                        if (position >= bytes.Length || bytes[position] != (byte) 'd') throw new InvalidDataException();
                        hasInfo = true;
                    }
                }
                ReadValue(depth + 1);
            }
            if (position >= bytes.Length || bytes[position++] != (byte) 'e') throw new InvalidDataException();
        }
    }
}
