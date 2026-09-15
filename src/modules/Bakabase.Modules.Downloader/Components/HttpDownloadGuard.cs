using System.Net;
using System.Net.Http.Headers;

namespace Bakabase.Modules.Downloader.Components;

internal sealed record HttpRemoteIdentity(string Url, long? Length, string? ETag,
    DateTimeOffset? LastModified, string? Md5Base64)
{
    public bool CanResume => Length is > 0 &&
        (TryGetStrongETag(out _) || LastModified.HasValue);

    internal bool TryGetStrongETag(out EntityTagHeaderValue? tag) =>
        EntityTagHeaderValue.TryParse(ETag, out tag) && !tag.IsWeak && tag.Tag != "*";
}

internal enum HttpGuardFailure {InvalidRange, RangeIgnored, RemoteChanged}

internal sealed class HttpDownloadProtocolException(HttpGuardFailure failure, string message) : Exception(message)
{
    public HttpGuardFailure Failure { get; } = failure;
}

/// <summary>
/// Validates a response before the download library can write it at a persisted chunk offset.
/// The supplied identity stays fixed for the whole attempt, including all parallel requests.
/// </summary>
internal sealed class HttpDownloadGuard(HttpClient hostClient, HttpRemoteIdentity identity, bool allowRanges)
    : HttpMessageHandler
{
    private volatile bool _transferStarted;
    private HttpDownloadProtocolException? _firstFailure;

    public bool TransferStarted
    {
        get => _transferStarted;
        set => _transferStarted = value;
    }

    public HttpDownloadProtocolException? FirstFailure => Volatile.Read(ref _firstFailure);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // HttpClient marks its input message as sent before invoking this handler. A different
        // message is required when forwarding through the host's configured HttpClient.
        using var forwarded = await CloneAsync(request, cancellationToken);
        forwarded.Headers.AcceptEncoding.Clear();
        forwarded.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        forwarded.Headers.IfRange = null;
        // Even a first, un-ranged GET must fetch the version identified by the probe. Otherwise
        // a response without its own validator could be checkpointed under an unrelated HEAD ETag.
        if (identity.TryGetStrongETag(out var expectedTag))
        {
            forwarded.Headers.IfMatch.Clear();
            forwarded.Headers.IfMatch.Add(expectedTag!);
        }
        else if (identity.LastModified is { } expectedModified)
            forwarded.Headers.IfUnmodifiedSince = expectedModified;
        if (!allowRanges)
        {
            forwarded.Headers.Range = null;
        }
        else if (forwarded.Headers.Range != null)
        {
            if (identity.TryGetStrongETag(out var tag))
                forwarded.Headers.IfRange = new RangeConditionHeaderValue(tag!);
            else if (identity.LastModified is { } modified)
                forwarded.Headers.IfRange = new RangeConditionHeaderValue(modified);
        }

        var response = await hostClient.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        try
        {
            // Authentication, throttling and other HTTP failures retain their normal handling.
            // Their error-page lengths and validators do not describe the download payload.
            if (response.StatusCode == HttpStatusCode.PreconditionFailed && identity.CanResume)
                throw new HttpDownloadProtocolException(HttpGuardFailure.RemoteChanged,
                    "The remote file no longer matches the download's original version.");
            if (!response.IsSuccessStatusCode) return response;

            ValidateIdentity(response);
            ValidateRange(forwarded.Headers.Range, response);

            if (!allowRanges || forwarded.Headers.Range != null && response.StatusCode == HttpStatusCode.OK)
            {
                // A successful full-body probe tells the library to use one connection.
                response.Content.Headers.ContentRange = null;
                response.Headers.AcceptRanges.Clear();
                response.Headers.AcceptRanges.Add("none");
            }

            // Keep the host's final RequestMessage/URI intact after redirects.
            return response;
        }
        catch (HttpDownloadProtocolException ex)
        {
            Interlocked.CompareExchange(ref _firstFailure, ex, null);
            response.Dispose();
            throw;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private void ValidateIdentity(HttpResponseMessage response)
    {
        var currentTag = response.Headers.ETag;
        var hasMatchingStrongTag = false;
        if (identity.ETag != null && currentTag != null)
        {
            if (!string.Equals(identity.ETag, currentTag.ToString(), StringComparison.Ordinal))
                throw new HttpDownloadProtocolException(HttpGuardFailure.RemoteChanged,
                    "The remote file's ETag changed during the download.");
            hasMatchingStrongTag = !currentTag.IsWeak && currentTag.Tag != "*";
        }

        if (!hasMatchingStrongTag && identity.LastModified is { } expectedModified &&
            response.Content.Headers.LastModified is { } currentModified && currentModified != expectedModified)
            throw new HttpDownloadProtocolException(HttpGuardFailure.RemoteChanged,
                "The remote file's last-modified time changed during the download.");

        var currentLength = response.StatusCode == HttpStatusCode.PartialContent
            ? response.Content.Headers.ContentRange?.Length
            : response.Content.Headers.ContentLength;
        if (identity.Length is { } expectedLength && currentLength is { } actualLength &&
            actualLength != expectedLength)
            throw new HttpDownloadProtocolException(HttpGuardFailure.RemoteChanged,
                "The remote file's total length changed during the download.");
    }

    private void ValidateRange(RangeHeaderValue? requestedRange, HttpResponseMessage response)
    {
        if (requestedRange == null)
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
                throw new HttpDownloadProtocolException(HttpGuardFailure.InvalidRange,
                    "The server returned partial content for a full-file request.");
            return;
        }

        if (response.Content.Headers.ContentEncoding.Any(encoding =>
                !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
            throw new HttpDownloadProtocolException(HttpGuardFailure.InvalidRange,
                "A compressed response cannot be written using HTTP byte-range offsets.");

        if (response.StatusCode == HttpStatusCode.OK)
        {
            if (!TransferStarted) return;
            throw new HttpDownloadProtocolException(HttpGuardFailure.RangeIgnored,
                "The server returned the whole file instead of the requested byte range.");
        }

        var range = requestedRange.Ranges.Count == 1 ? requestedRange.Ranges.Single() : null;
        var received = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent ||
            !string.Equals(requestedRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            range?.From == null || range.To == null ||
            received == null || !string.Equals(received.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            received.From != range.From || received.To != range.To || received.Length == null ||
            received.Length <= received.To ||
            identity.Length is { } expectedLength && received.Length != expectedLength)
            throw new HttpDownloadProtocolException(HttpGuardFailure.InvalidRange,
                "The server returned a Content-Range that does not match the requested bytes.");

        if (response.Content.Headers.ContentLength is { } contentLength &&
            contentLength != range.To.Value - range.From.Value + 1)
            throw new HttpDownloadProtocolException(HttpGuardFailure.InvalidRange,
                "The range response's body length does not match its Content-Range.");
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        try
        {
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in request.Options)
                clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            if (request.Content != null)
            {
                clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken));
                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }
}
