using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public partial class ExHentaiClient
{
    private const string RecoverableImageNodeKey = "Bakabase.ExHentai.RecoverableImageNode";
    private const string ImageNodeRecoveryExhaustedKey = "Bakabase.ExHentai.ImageNodeRecoveryExhausted";

    // Callers must not repeat a spent 50-hit reload through their outer transient retry loop.
    // A spent free-original recovery also receives this marker; paid originals keep their policy.
    public static bool IsImageNodeRecoveryExhausted(Exception error)
    {
        var pending = new Stack<Exception>();
        pending.Push(error);
        for (var visited = 0; pending.Count > 0 && visited < 64; visited++)
        {
            var current = pending.Pop();
            if (current.Data[ImageNodeRecoveryExhaustedKey] is true) return true;
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions) pending.Push(inner);
            else if (current.InnerException != null) pending.Push(current.InnerException);
        }
        return false;
    }

    private readonly record struct ImageResponseDetails(Uri FinalUri, HttpStatusCode? Status = null,
        string? ContentType = null, long? Length = null, string? Format = null);

    private static string PublicImagePage(Uri uri) => uri.GetLeftPart(UriPartial.Path);

    private static string BuildImageDiagnostic(Uri? pageUri, ImageResponseDetails details, string reason)
    {
        var page = pageUri == null ? "direct image" : PublicImagePage(pageUri);
        var number = pageUri == null ? null : Regex.Match(pageUri.LocalPath, @"/\d+-(\d+)/?$");
        var pageNumber = number?.Success == true ? number.Groups[1].Value : "unknown";
        // These fields are bounded metadata, never response text, a signed image URL, or a token.
        var contentType = details.ContentType != null && Regex.IsMatch(details.ContentType,
            @"\A[a-zA-Z0-9.+-]+/[a-zA-Z0-9.+-]+\z") ? details.ContentType : "unknown";
        return $"E-Hentai image download failed: {reason}; page={page}; pageNumber={pageNumber}; " +
               $"finalHost={details.FinalUri.Host}; status={(details.Status.HasValue ? ((int)details.Status.Value).ToString() : "unknown")}; " +
               $"contentType={contentType}; length={details.Length?.ToString() ?? "unknown"}; format={details.Format ?? "unknown"}.";
    }

    private static InvalidDataException ImageDataError(Uri? pageUri, ImageResponseDetails details, string reason,
        bool recoverable = false)
    {
        var error = new InvalidDataException(BuildImageDiagnostic(pageUri, details, reason));
        if (recoverable) error.Data[RecoverableImageNodeKey] = true;
        return error;
    }

    private static HttpRequestException ImageHttpError(Uri? pageUri, ImageResponseDetails details, string reason,
        HttpRequestError requestError = HttpRequestError.Unknown, bool recoverable = false,
        bool bodyTransport = false, bool rejectedAuthentication = false)
    {
        // Do not keep an inner transport exception: it may contain a signed URL or the nl token.
        // Preserve the classifier's distinction between a broken TLS connection and a rejected
        // certificate, using only a new fixed-message authentication cause for the latter.
        var error = new HttpRequestException(requestError, BuildImageDiagnostic(pageUri, details, reason),
            inner: rejectedAuthentication ? new AuthenticationException("TLS authentication failed.") : null,
            statusCode: bodyTransport ? null : details.Status);
        if (recoverable) error.Data[RecoverableImageNodeKey] = true;
        return error;
    }

    private static TaskCanceledException ImageTimeoutError(Uri? pageUri, ImageResponseDetails details, string reason,
        CancellationToken token, bool recoverable = false)
    {
        // Keep the established timeout contract and transient classification without preserving
        // HttpClient's original message or inner exception, which can include a signed URL.
        var error = new TaskCanceledException(BuildImageDiagnostic(pageUri, details, reason),
            new TimeoutException("The E-Hentai request timed out."), token);
        if (recoverable) error.Data[RecoverableImageNodeKey] = true;
        return error;
    }

    private static HttpRequestError SafeImageRequestError(HttpRequestException error) =>
        error.HttpRequestError == HttpRequestError.Unknown && error.InnerException is IOException or SocketException
            ? HttpRequestError.ConnectionError : error.HttpRequestError;

    private static bool IsHathNodeTlsFailure(Uri uri, HttpRequestException error) =>
        uri.Scheme == "https" && uri.Host.EndsWith(".hath.network", StringComparison.OrdinalIgnoreCase) &&
        error.HttpRequestError == HttpRequestError.SecureConnectionError &&
        error.InnerException is AuthenticationException;

    private static bool RecoverableNodeStatus(HttpStatusCode? status) => status is
        HttpStatusCode.Forbidden or HttpStatusCode.NotFound || status.HasValue &&
        (int)status.Value >= 500 && (int)status.Value != 509;

    private static string? ReadNodeReloadToken(CQ page, Uri pageUri)
    {
        if (!IsAccountHost(pageUri) || !Regex.IsMatch(pageUri.LocalPath, @"\A/s/[a-fA-F0-9]+/\d+-\d+/?\z"))
            return null;
        var attributes = new[] {page["#loadfail"].Attr("onclick"), page["#img"].Attr("onerror")};
        var tokens = new List<string>();
        for (var i = 0; i < attributes.Length; i++)
        {
            var attribute = attributes[i];
            if (string.IsNullOrWhiteSpace(attribute)) continue;
            // Recognize only the site's literal call, optionally with its known onerror reset.
            // Never execute JS, accept expressions, or invent the donor-only nl=1 shortcut.
            var prefix = i == 1 ? @"(?:this\.onerror\s*=\s*null\s*;\s*)?" : "";
            var match = Regex.Match(attribute, @"\A\s*" + prefix +
                "(?:return\\s+)?nl\\(\\s*(?<quote>['\"])(?<token>[a-zA-Z0-9]{1,32}-[a-zA-Z0-9]{1,64})\\k<quote>\\s*\\)\\s*;?\\s*\\z",
                RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            tokens.Add(match.Groups["token"].Value);
        }
        var distinct = tokens.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length != 1) return null;
        return distinct[0];
    }

    private static Uri WithNodeReloadToken(Uri uri, string token)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !Uri.UnescapeDataString(pair.Split('=', 2)[0].Replace('+', ' '))
                .Equals("nl", StringComparison.OrdinalIgnoreCase)).ToList();
        query.Add("nl=" + Uri.EscapeDataString(token));
        return new UriBuilder(uri) {Query = string.Join("&", query), Fragment = ""}.Uri;
    }

    private static string? KnownImageUrlError(Uri uri)
    {
        if (IsAccountHost(uri) || uri.Host.Equals("ehgt.org", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(uri.LocalPath, @"/509s?\.gif$", RegexOptions.IgnoreCase)) return "image viewing quota exceeded";
            if (uri.LocalPath.Contains("sadpanda", StringComparison.OrdinalIgnoreCase)) return "access denied (Sad Panda)";
            if (uri.LocalPath.EndsWith("/blank.gif", StringComparison.OrdinalIgnoreCase)) return "blank image placeholder";
            if (uri.LocalPath.Contains("bounce_login", StringComparison.OrdinalIgnoreCase)) return "login required";
        }
        if (uri.Host.Equals("forums.e-hentai.org", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(uri.Query, @"(?:[?&])act=Login(?:&|$)", RegexOptions.IgnoreCase)) return "login required";
        return null;
    }

    private static string? KnownImageContentError(string content)
    {
        var page = new CQ(content);
        if (page["input[type=password]"].Any()) return "login required";
        var text = string.Join(" ", page.Document.ChildNodes.SelectMany(ReadVisibleText));
        text = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ");
        if (Regex.IsMatch(text, @"(?:error\s*509|bandwidth\s+exceeded|could\s+not\s+get\s+dispatch\s+for\s+image|image\s+(?:viewing\s+)?(?:limit|quota)\s+(?:exceeded|reached)|exceeded\s+(?:your\s+)?image\s+(?:viewing\s+)?(?:limits?|quota))", RegexOptions.IgnoreCase))
            return "image viewing quota exceeded";
        if (Regex.IsMatch(text, @"(?:you\s+(?:must|need to)\s+(?:log|sign)\s+in|not\s+logged\s+in|login\s+required|please\s+(?:log|sign)\s+in)", RegexOptions.IgnoreCase))
            return "login required";
        if (page["#challenge-form"].Any() ||
            Regex.IsMatch(text, @"(?:cloudflare\s+challenge|checking\s+your\s+browser|verify\s+(?:that\s+)?you\s+are\s+human)", RegexOptions.IgnoreCase) ||
            content.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase) && text.Contains("Just a moment", StringComparison.OrdinalIgnoreCase))
            return "access challenge";
        if (Regex.IsMatch(text, @"(?:\b(?:IP|account)\b.*?\b(?:banned|suspended)\b|temporarily\s+banned|excessive\s+pageloads|opening\s+pages\s+too\s+fast)", RegexOptions.IgnoreCase))
            return "access banned or rate limited";
        if (Regex.IsMatch(text, @"(?:insufficient\s+(?:GP|funds)|not\s+enough\s+(?:GP|funds)|requires?\s+GP|GP\s+(?:balance\s+)?(?:is\s+)?(?:insufficient|too\s+low))", RegexOptions.IgnoreCase))
            return "original-image GP or funds restriction";
        return null;
    }

    private static string? ImageFormat(byte[] data) =>
        data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff ? "JPEG" :
        data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}) ? "PNG" :
        data.Length >= 6 && (data.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || data.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) ? "GIF" :
        data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8) ? "WebP" :
        data.Length >= 12 && data.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
        (data.AsSpan(8, 4).SequenceEqual("avif"u8) || data.AsSpan(8, 4).SequenceEqual("avis"u8)) ? "AVIF" : null;

    private static string? ImageErrorText(byte[] bytes) => bytes.Length == 0 ? null :
        KnownImageContentError(Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 64 * 1024))));
}
