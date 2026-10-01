using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using CsQuery;
using Microsoft.Extensions.Logging;

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

        Uri? reloadUri = null;
        string? reloadToken = null;
        Uri? originalEntry = null;
        Uri? originalRecoveryTarget = null;
        var recoveringOriginal = false;
        var originalRequested = false;
        var regularFallback = !options.PreferOriginal;
        Func<Uri, ExHentaiOriginalImageInfo>? originalInfo = null;
        (string Html, DateTime? ServerDateUtc, long CompletedAt, ImageResponseDetails Details)? originalPage = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                originalRequested = false;
                var requestedPage = recoveringOriginal || attempt == 0 ? pageUri : reloadUri!;
                var (html, serverDate, pageCompletedAt, pageDetails) =
                    recoveringOriginal && attempt > 0 ? originalPage!.Value :
                        await GetExHentaiPageAsync(requestedPage, options.RequestContext, ct, pageUri,
                            retryTransient: attempt == 0);
                if (!recoveringOriginal && serverDate.HasValue && Math.Abs((serverDate.Value - DateTime.UtcNow).TotalSeconds) > 60)
                    serverDate = null;
                if (options.PreferOriginal && attempt == 0)
                    originalPage = (html, serverDate, pageCompletedAt, pageDetails);
                var page = new CQ(html);
                if (attempt == 0)
                {
                    reloadToken = ReadNodeReloadToken(page, pageUri);
                    reloadUri = reloadToken == null ? null : WithNodeReloadToken(pageUri, reloadToken);
                }
                var imageSource = page["#img"].Attr("src");
                if (string.IsNullOrWhiteSpace(imageSource))
                    throw ImageDataError(pageUri, pageDetails, "the image-viewing page did not contain an image");
                if (!Uri.TryCreate(requestedPage, WebUtility.HtmlDecode(imageSource), out var imageUrl))
                    throw ImageDataError(pageUri, pageDetails, "the image-viewing page contained an invalid image URL");
                Uri? originalUrl = null;
                long? originalSize = null;
                foreach (var anchor in page["a[href]"])
                {
                    if (!Uri.TryCreate(requestedPage, WebUtility.HtmlDecode(anchor.GetAttribute("href")), out var candidate) ||
                        !IsOriginalEndpoint(candidate)) continue;
                    if (!IsAccountHost(candidate))
                        throw ImageDataError(pageUri, pageDetails, "the original-image link pointed outside E-Hentai");
                    originalUrl = candidate;
                    originalSize = ParseOriginalSizeUpperBound(anchor.Cq().Text());
                    break;
                }
                originalEntry = originalUrl;
                var useOriginal = options.PreferOriginal && (attempt == 0 || recoveringOriginal);
                regularFallback = !useOriginal || originalUrl == null && !IsOriginalEndpoint(imageUrl);
                RejectErrorImageUrl(imageUrl, pageUri);
                var target = recoveringOriginal ? originalRecoveryTarget! :
                    useOriginal && originalUrl != null ? originalUrl : imageUrl;
                ExHentaiOriginalImageInfo BuildOriginalInfo(Uri uri) => new()
                {
                    OriginalUrl = uri.AbsoluteUri, OriginalSizeBytes = originalSize, PageUrl = pageUrl,
                    ServerTimeUtc = serverDate?.Add(Stopwatch.GetElapsedTime(pageCompletedAt))
                };
                originalInfo = BuildOriginalInfo;
                void CheckFreeRecovery(ExHentaiOriginalImageInfo info)
                {
                    if (attempt > 0 && options.CanRecoverOriginalWithoutGp?.Invoke(info) != true)
                        throw new InvalidOperationException("Original-image node recovery requires a confirmed free request.");
                }
                using var imageResponse = await SendImageRequestAsync(target, options.RequestContext, ct,
                    beforeOriginal: useOriginal ? async uri =>
                    {
                        var info = BuildOriginalInfo(uri);
                        CheckFreeRecovery(info);
                        await options.BeforeOriginalDownload!(info, ct);
                        originalRequested = true;
                    } : null, beforeOriginalSend: useOriginal ? (uri, token) =>
                    {
                        var info = BuildOriginalInfo(uri);
                        CheckFreeRecovery(info);
                        return options.BeforeOriginalSend!(info, token);
                    } : null, diagnosticPage: pageUri);
                var result = await ReadImageBytesAsync(imageResponse, ct, pageUri);
                return new ExHentaiDownloadedImage
                {
                    Data = result.Data, ContentType = result.ContentType,
                    IsOriginal = options.PreferOriginal && originalRequested,
                    OriginalUnavailable = options.PreferOriginal && !originalRequested
                };
            }
            catch (Exception error) when (regularFallback && !originalRequested && attempt == 0 && reloadUri != null &&
                                          error.Data[RecoverableImageNodeKey] is true && !ct.IsCancellationRequested)
            {
                // One page-provided reload costs viewing quota. Do not keep retrying the same
                // signed image URL, invent nl=1, or switch normal downloads to fullimg.
                recoveringOriginal = false;
            }
            catch (Exception error) when (originalRequested && options.PreferOriginal && attempt == 0 && originalEntry != null &&
                reloadToken != null && options.CanRecoverOriginalWithoutGp != null &&
                error.Data[RecoverableImageNodeKey] is true && !ct.IsCancellationRequested)
            {
                if (!options.CanRecoverOriginalWithoutGp(originalInfo!(originalEntry))) throw;
                // This changes only the original node. Both spending callbacks and the pure free
                // check run again, including immediately after request pacing before transmission.
                originalRecoveryTarget = WithNodeReloadToken(originalEntry, reloadToken);
                recoveringOriginal = true;
            }
            catch (Exception error) when (attempt == 1 && !ct.IsCancellationRequested)
            {
                error.Data[ImageNodeRecoveryExhaustedKey] = true;
                throw;
            }
        }
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
        var (html, _, _, _) = await GetExHentaiPageAsync(new Uri(AccountBalanceUrl), context, ct);
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
        if (node.NodeType == NodeType.TEXT_NODE)
        {
            yield return node.NodeValue;
            yield break;
        }
        if (string.Equals(node.NodeName, "script", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.NodeName, "style", StringComparison.OrdinalIgnoreCase)) yield break;
        // CsQuery's doctype/comment leaves inherit DomObject.ChildNodes, which returns null.
        // They contain no visible text; only containers participate in document-order DFS.
        var children = node.ChildNodes;
        if (children == null) yield break;
        foreach (var child in children)
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

    private async Task<(string Html, DateTime? ServerDateUtc, long CompletedAt, ImageResponseDetails Details)> GetExHentaiPageAsync(Uri uri,
        ExHentaiRequestContext? context, CancellationToken ct, Uri? diagnosticPage = null, bool retryTransient = true)
    {
        for (var attempt = 1;; attempt++)
        {
            // One permit per attempt, including body reads. Cancellation also interrupts a
            // page waiting behind another caller; a retry must reacquire its own permit.
            await _lock.WaitAsync(ct);
            try
            {
                using var response = await SendImageRequestAsync(uri, context, ct, pageOnly: true, diagnosticPage: diagnosticPage);
                var details = new ImageResponseDetails(response.RequestMessage?.RequestUri ?? uri,
                    response.StatusCode, response.Content.Headers.ContentType?.MediaType,
                    response.Content.Headers.ContentLength);
                string html;
                using var bodyCancellation = CreateImageBodyCancellation(ct);
                try { html = await response.Content.ReadAsStringAsync(bodyCancellation.Token); }
                catch (HttpRequestException error)
                {
                    throw ImageHttpError(diagnosticPage, details, "page body transport failed", SafeImageRequestError(error),
                        bodyTransport: true, rejectedAuthentication: error.InnerException is System.Security.Authentication.AuthenticationException);
                }
                catch (HttpIOException error)
                {
                    throw ImageHttpError(diagnosticPage, details, "page body transport failed", error.HttpRequestError, bodyTransport: true);
                }
                catch (OperationCanceledException error) when (!ct.IsCancellationRequested &&
                    (bodyCancellation.IsCancellationRequested || error.InnerException is TimeoutException))
                {
                    throw ImageTimeoutError(diagnosticPage, details, "page request timed out", bodyCancellation.Token);
                }
                var completedAt = Stopwatch.GetTimestamp();
                details = details with {Length = Encoding.UTF8.GetByteCount(html)};
                if (diagnosticPage != null && KnownImageContentError(html) is { } reason)
                    throw ImageDataError(diagnosticPage, details, reason);
                if (!response.IsSuccessStatusCode) throw ImageHttpError(diagnosticPage, details, "HTTP request failed");
                if (IsBanned(html)) throw ImageDataError(diagnosticPage, details, "access banned or rate limited");
                return (html, response.Headers.Date?.UtcDateTime, completedAt, details);
            }
            catch (Exception e) when (retryTransient && attempt < MaxHtmlAttempts && TransientNetworkError.IsTransient(e, ct))
            {
                Logger.LogWarning("Transient network error requesting {Url}, retrying ({Attempt}/{MaxAttempts})",
                    PublicImagePage(uri), attempt + 1, MaxHtmlAttempts);
            }
            finally
            {
                _lock.Release();
            }

            // Back off outside the page gate. Only safe page loads retry here; fullimg and
            // image-byte requests and quota-consuming nl page loads stay single-attempt.
            await Task.Delay(
                TransientNetworkError.GetBackoffDelay(attempt - 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)),
                ct);
        }
    }

    private async Task<HttpResponseMessage> SendImageRequestAsync(Uri uri, ExHentaiRequestContext? context,
        CancellationToken ct, bool pageOnly = false, Func<Uri, Task>? beforeOriginal = null,
        Func<Uri, CancellationToken, Task>? beforeOriginalSend = null, Uri? diagnosticPage = null)
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
            RejectErrorImageUrl(uri, diagnosticPage);
            if (IsOriginalEndpoint(uri))
            {
                if (!accountHost || beforeOriginal == null)
                    throw new InvalidOperationException("An original-image request requires a spending check.");
                await beforeOriginal(uri);
                ct.ThrowIfCancellationRequested();
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Options.Set(ThirdPartyRequestOptions.RequestLogKey,
                pageOnly ? PublicImagePage(uri) : $"E-Hentai image download ({uri.Host})");
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
                request.Options.Set(ThirdPartyRequestOptions.AccountKey, "image-external:" + uri.Host);
                request.Options.Set(ThirdPartyRequestOptions.Cookie, string.Empty);
                request.Options.Set(ThirdPartyRequestOptions.SuppressSensitiveHeaders, true);
            }
            HttpResponseMessage response;
            try
            {
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (HttpRequestException error)
            {
                throw ImageHttpError(diagnosticPage, new ImageResponseDetails(uri, error.StatusCode),
                    "image transport failed", SafeImageRequestError(error), recoverable: !pageOnly && !accountHost &&
                        (TransientNetworkError.IsTransient(error, ct) || RecoverableNodeStatus(error.StatusCode)),
                    rejectedAuthentication: error.InnerException is System.Security.Authentication.AuthenticationException);
            }
            catch (HttpIOException error)
            {
                throw ImageHttpError(diagnosticPage, new ImageResponseDetails(uri), "image transport failed",
                    error.HttpRequestError, recoverable: !pageOnly && !accountHost);
            }
            catch (OperationCanceledException error) when (!ct.IsCancellationRequested && error.InnerException is TimeoutException)
            {
                throw ImageTimeoutError(diagnosticPage, new ImageResponseDetails(uri), "image request timed out",
                    error.CancellationToken, recoverable: !pageOnly && !accountHost);
            }
            response.RequestMessage ??= request;
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

    private static void RejectErrorImageUrl(Uri uri, Uri? diagnosticPage = null)
    {
        if (KnownImageUrlError(uri) is { } reason)
            throw ImageDataError(diagnosticPage, new ImageResponseDetails(uri), reason,
                recoverable: reason == "blank image placeholder");
    }

    private CancellationTokenSource CreateImageBodyCancellation(CancellationToken ct)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (HttpClient.Timeout != Timeout.InfiniteTimeSpan) cancellation.CancelAfter(HttpClient.Timeout);
        return cancellation;
    }

    private async Task<(byte[] Data, string? ContentType)> ReadImageBytesAsync(HttpResponseMessage response,
        CancellationToken ct, Uri? diagnosticPage = null)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var finalUri = response.RequestMessage?.RequestUri ?? diagnosticPage ?? new Uri(Domain);
        var details = new ImageResponseDetails(finalUri, response.StatusCode, contentType,
            response.Content.Headers.ContentLength);
        byte[] bytes;
        using var bodyCancellation = CreateImageBodyCancellation(ct);
        try { bytes = await response.Content.ReadAsByteArrayAsync(bodyCancellation.Token); }
        catch (HttpRequestException error)
        {
            throw ImageHttpError(diagnosticPage, details, "image body transport failed", SafeImageRequestError(error),
                recoverable: !IsAccountHost(finalUri), bodyTransport: true,
                rejectedAuthentication: error.InnerException is System.Security.Authentication.AuthenticationException);
        }
        catch (HttpIOException error)
        {
            throw ImageHttpError(diagnosticPage, details, "image body transport failed", error.HttpRequestError,
                recoverable: !IsAccountHost(finalUri), bodyTransport: true);
        }
        catch (OperationCanceledException error) when (!ct.IsCancellationRequested &&
            (bodyCancellation.IsCancellationRequested || error.InnerException is TimeoutException))
        {
            throw ImageTimeoutError(diagnosticPage, details, "image body request timed out", bodyCancellation.Token,
                recoverable: !IsAccountHost(finalUri));
        }
        details = details with {Length = bytes.Length, Format = ImageFormat(bytes)};
        if ((int)response.StatusCode == 509) throw ImageDataError(diagnosticPage, details, "image viewing quota exceeded");
        if ((details.Format == null || contentType is "text/html" or "application/xhtml+xml") && ImageErrorText(bytes) is { } knownReason)
            throw ImageDataError(diagnosticPage, details, knownReason);
        if (!response.IsSuccessStatusCode)
            throw ImageHttpError(diagnosticPage, details, "HTTP request failed", recoverable:
                !IsAccountHost(finalUri) && RecoverableNodeStatus(response.StatusCode));
        if (contentType is "text/html" or "application/xhtml+xml" || details.Format == null)
            throw ImageDataError(diagnosticPage, details, bytes.Length == 0 ? "empty image response" :
                bytes.Length >= 16 && bytes.Take(Math.Min(bytes.Length, 64)).All(value => value == 0)
                    ? "image data begins with zero bytes" :
                contentType is "text/html" or "application/xhtml+xml" ? "HTML response instead of image data" : "unrecognized image data",
                recoverable: !IsAccountHost(finalUri));
        return (bytes, contentType);
    }
}
