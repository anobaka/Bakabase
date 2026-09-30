using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using CsQuery;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public partial class ExHentaiClient
{
    public const string AccountBalanceUrl = "https://e-hentai.org/exchange.php?t=gp";

    public async Task<ExHentaiDownloadedImage> DownloadImage(string pageUrl,
        ExHentaiImageDownloadOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ct.ThrowIfCancellationRequested();
        if (options.PreferOriginal && (options.BeforeOriginalDownload == null || options.BeforeOriginalSend == null))
            throw new InvalidOperationException("Original-image downloads require a pre-download spending check.");
        if (options.PreferOriginal) RequireAccount(options.RequestContext);
        var pageUri = ValidateImageRequestUri(pageUrl);
        if (!IsAccountHost(pageUri) || IsOriginalEndpoint(pageUri))
            throw new InvalidDataException("An E-Hentai image-viewing page is required.");

        using var pageResponse = await SendImageRequestAsync(pageUri, options.RequestContext, ct,
            pageOnly: true);
        pageResponse.EnsureSuccessStatusCode();
        var html = await pageResponse.Content.ReadAsStringAsync(ct);
        var pageCompletedAt = Stopwatch.GetTimestamp();
        var serverDate = pageResponse.Headers.Date?.UtcDateTime;
        if (serverDate.HasValue && Math.Abs((serverDate.Value - DateTime.UtcNow).TotalSeconds) > 60)
            serverDate = null;
        ThrowIfBanned(html);
        var page = new CQ(html);
        var imageSource = page["#img"].Attr("src");
        if (string.IsNullOrWhiteSpace(imageSource))
            throw new InvalidDataException("The image-viewing page did not contain an image.");
        var imageUrl = new Uri(pageUri, WebUtility.HtmlDecode(imageSource));
        RejectErrorImageUrl(imageUrl);

        Uri? originalUrl = null;
        long? originalSize = null;
        foreach (var anchor in page["a[href]"])
        {
            if (!Uri.TryCreate(pageUri, WebUtility.HtmlDecode(anchor.GetAttribute("href")), out var candidate) ||
                !IsOriginalEndpoint(candidate)) continue;
            if (!IsAccountHost(candidate))
                throw new InvalidDataException("The original-image link pointed outside E-Hentai.");
            originalUrl = candidate;
            originalSize = ParseOriginalSizeUpperBound(anchor.Cq().Text());
            break;
        }
        var target = options.PreferOriginal && originalUrl != null ? originalUrl : imageUrl;
        var originalRequested = false;
        ExHentaiOriginalImageInfo BuildOriginalInfo(Uri uri) => new()
        {
            OriginalUrl = uri.AbsoluteUri, OriginalSizeBytes = originalSize, PageUrl = pageUrl,
            ServerTimeUtc = serverDate?.Add(Stopwatch.GetElapsedTime(pageCompletedAt))
        };
        using var imageResponse = await SendImageRequestAsync(target, options.RequestContext, ct,
            beforeOriginal: options.PreferOriginal ? async uri =>
            {
                await options.BeforeOriginalDownload!(BuildOriginalInfo(uri), ct);
                originalRequested = true;
            } : null, beforeOriginalSend: options.PreferOriginal ? (uri, token) =>
                options.BeforeOriginalSend!(BuildOriginalInfo(uri), token) : null);
        var result = await ReadImageBytesAsync(imageResponse, ct);
        return new ExHentaiDownloadedImage
        {
            Data = result.Data, ContentType = result.ContentType,
            IsOriginal = options.PreferOriginal && originalRequested,
            OriginalUnavailable = options.PreferOriginal && !originalRequested
        };
    }

    public Task<ExHentaiAccountBalance> GetAccountBalance(CancellationToken ct = default)
    {
        var cookie = HttpClient.DefaultRequestHeaders.TryGetValues("Cookie", out var values)
            ? string.Join("; ", values) : string.Empty;
        return GetAccountBalance(new ExHentaiRequestContext(cookie), ct);
    }

    public async Task<ExHentaiAccountBalance> GetAccountBalance(ExHentaiRequestContext context,
        CancellationToken ct = default)
    {
        RequireAccount(context);
        using var response = await SendImageRequestAsync(new Uri(AccountBalanceUrl), context, ct, pageOnly: true);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);
        ThrowIfBanned(html);
        var page = new CQ(html);
        if (page["input[type=password]"].Any() ||
            Regex.IsMatch(page.Text(), @"(?:you (?:must|need to) (?:log|sign) in|not logged in)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("The GP balance page did not confirm a logged-in account.");
        // EhViewer HomeParser.parseFunds reads these exact Available labels from exchange.php.
        // Restrict to currency balances, rather than prices or totals elsewhere in the exchange.
        // Text() concatenates adjacent block elements ("kGPAvailable") without a separator.
        // Traverse the DOM in document order so nested strong/span numbers stay between their
        // labels and units, while script/style literals cannot masquerade as account balances.
        var visibleText = string.Join(" ", page.Document.ChildNodes.SelectMany(ReadVisibleText));
        var text = Regex.Replace(WebUtility.HtmlDecode(visibleText), @"\s+", " ");
        var gp = ParseAvailableBalance(text, @"kGP|GP", required: true);
        var credits = ParseAvailableBalance(text, "Credits", required: false);
        return new ExHentaiAccountBalance {GpBalance = gp!.Value, CreditsBalance = credits};
    }

    private static IEnumerable<string> ReadVisibleText(IDomObject node)
    {
        if (node.NodeName.Equals("script", StringComparison.OrdinalIgnoreCase) ||
            node.NodeName.Equals("style", StringComparison.OrdinalIgnoreCase)) yield break;
        if (node.NodeType == NodeType.TEXT_NODE)
        {
            yield return node.NodeValue;
            yield break;
        }
        foreach (var child in node.ChildNodes)
        foreach (var text in ReadVisibleText(child))
            yield return text;
    }

    private static long? ParseAvailableBalance(string text, string units, bool required)
    {
        var matches = Regex.Matches(text,
            @"\bAvailable\s*:\s*(?<amount>(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?)\s*(?<unit>" + units + @")\b",
            RegexOptions.IgnoreCase);
        var balances = new List<long>();
        foreach (Match match in matches)
        {
            if (!decimal.TryParse(match.Groups["amount"].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value)) continue;
            value *= match.Groups["unit"].Value.Equals("kGP", StringComparison.OrdinalIgnoreCase) ? 1000 : 1;
            if (value < 0 || value > long.MaxValue || value != decimal.Truncate(value)) continue;
            balances.Add((long) value);
        }
        var distinct = balances.Distinct().ToArray();
        if (distinct.Length == 1) return distinct[0];
        if (!required && distinct.Length == 0) return null;
        throw new InvalidDataException("The account's available currency balance could not be determined reliably.");
    }

    private static void RequireAccount(ExHentaiRequestContext? context)
    {
        if (context == null || !Regex.IsMatch(context.Cookie, @"(?:^|;)\s*ipb_member_id\s*=\s*\d+\s*(?:;|$)") ||
            !Regex.IsMatch(context.Cookie, @"(?:^|;)\s*ipb_pass_hash\s*=\s*[^;\s]+"))
            throw new InvalidOperationException("An authenticated E-Hentai cookie snapshot is required.");
    }

    private static long? ParseOriginalSizeUpperBound(string label)
    {
        var match = Regex.Match(label,
            @"\bDownload\s+original\b.*?\b(?<size>\d+(?:\.\d+)?)\s*(?<unit>KiB|MiB|GiB|KB|MB|GB|bytes?|B)\b",
            RegexOptions.IgnoreCase);
        if (!match.Success || !decimal.TryParse(match.Groups["size"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value)) return null;
        var unit = match.Groups["unit"].Value.ToUpperInvariant();
        decimal multiplier = unit switch
        {
            "KIB" or "KB" => 1024, "MIB" or "MB" => 1024 * 1024,
            "GIB" or "GB" => 1024 * 1024 * 1024, _ => 1
        };
        // The page displays a rounded size. One unit of its last displayed decimal place gives
        // a conservative upper bound instead of underestimating a spending boundary.
        var raw = match.Groups["size"].Value;
        var decimals = raw.Contains('.') ? raw.Length - raw.IndexOf('.') - 1 : 0;
        var uncertainty = multiplier > 1 ? 1m / (decimal) Math.Pow(10, decimals) : 0;
        var upper = decimal.Ceiling((value + uncertainty) * multiplier);
        return upper is > 0 and <= long.MaxValue ? (long) upper : null;
    }

    private Task<HttpResponseMessage> SendExHentaiRequestAsync(string url, CancellationToken ct) =>
        SendImageRequestAsync(ValidateImageRequestUri(url), null, ct, pageOnly: true);

    private async Task<HttpResponseMessage> SendImageRequestAsync(Uri uri, ExHentaiRequestContext? context,
        CancellationToken ct, bool pageOnly = false, Func<Uri, Task>? beforeOriginal = null,
        Func<Uri, CancellationToken, Task>? beforeOriginalSend = null)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            ct.ThrowIfCancellationRequested();
            uri = ValidateImageRequestUri(uri.AbsoluteUri);
            if (!visited.Add(uri.AbsoluteUri)) throw new InvalidDataException("An image request entered a redirect loop.");
            var accountHost = IsAccountHost(uri);
            if (pageOnly && (!accountHost || IsOriginalEndpoint(uri)))
                throw new InvalidDataException("A gallery or account page redirected to an unexpected destination.");
            RejectErrorImageUrl(uri);
            if (IsOriginalEndpoint(uri))
            {
                if (!accountHost || beforeOriginal == null)
                    throw new InvalidOperationException("An original-image request requires a spending check.");
                await beforeOriginal(uri);
                ct.ThrowIfCancellationRequested();
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (context != null || !pageOnly || !accountHost)
                request.Options.Set(ThirdPartyRequestOptions.SkipConfiguredHeaders, true);
            if (IsOriginalEndpoint(uri))
            {
                var sendingUri = uri;
                if (beforeOriginalSend == null)
                    throw new InvalidOperationException("An original-image request requires a final spending check.");
                request.Options.Set(ThirdPartyRequestOptions.BeforeSend, token => beforeOriginalSend(sendingUri, token));
            }
            if (accountHost && context != null)
            {
                request.Options.Set(ThirdPartyRequestOptions.AccountKey, context.AccountKey + ":" + uri.Host);
                request.Options.Set(ThirdPartyRequestOptions.Cookie, context.Cookie);
                request.Headers.TryAddWithoutValidation("Cookie", context.Cookie);
            }
            else if (!accountHost)
            {
                request.Options.Set(ThirdPartyRequestOptions.Cookie, string.Empty);
                request.Options.Set(ThirdPartyRequestOptions.SuppressSensitiveHeaders, true);
            }
            var response = await HttpClient.SendAsync(request, ct);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location == null) throw new InvalidDataException("An image redirect did not provide a destination.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException("An image request exceeded the redirect limit.");
    }

    private static Uri ValidateImageRequestUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("The image URL is invalid.");
        if (IsAccountHost(uri) && uri.Scheme != "https")
            throw new InvalidDataException("Account requests require HTTPS.");
        return uri;
    }

    private static bool IsAccountHost(Uri uri) => uri.Host.Equals("e-hentai.org", StringComparison.OrdinalIgnoreCase) ||
                                                  uri.Host.Equals("exhentai.org", StringComparison.OrdinalIgnoreCase);

    private static bool IsOriginalEndpoint(Uri uri) => uri.LocalPath.Equals("/fullimg.php", StringComparison.OrdinalIgnoreCase) ||
        uri.LocalPath.Equals("/fullimg", StringComparison.OrdinalIgnoreCase) ||
        uri.LocalPath.StartsWith("/fullimg/", StringComparison.OrdinalIgnoreCase);

    private static void RejectErrorImageUrl(Uri uri)
    {
        if ((IsAccountHost(uri) || uri.Host.Equals("ehgt.org", StringComparison.OrdinalIgnoreCase)) &&
            (uri.AbsolutePath.EndsWith("/509.gif", StringComparison.OrdinalIgnoreCase) ||
             uri.AbsolutePath.Contains("sadpanda", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("E-Hentai returned an image-limit or access-error image.");
    }

    private static async Task<(byte[] Data, string? ContentType)> ReadImageBytesAsync(HttpResponseMessage response,
        CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (contentType is "text/html" or "application/xhtml+xml" || !HasImageSignature(bytes))
            throw new InvalidDataException("The image request returned an error page or invalid image data.");
        return (bytes, contentType);
    }

    private static bool HasImageSignature(byte[] data) =>
        data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff ||
        data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}) ||
        data.Length >= 6 && (data.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || data.AsSpan(0, 6).SequenceEqual("GIF89a"u8)) ||
        data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8) ||
        data.Length >= 12 && data.AsSpan(4, 4).SequenceEqual("ftyp"u8) &&
        (data.AsSpan(8, 4).SequenceEqual("avif"u8) || data.AsSpan(8, 4).SequenceEqual("avis"u8));
}
