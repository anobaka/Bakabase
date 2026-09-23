using System.Net.Http.Headers;
using Bakabase.Modules.RemoteAccess.Components.Pairing;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// Puts the device signature on an outgoing request.
/// </summary>
/// <remarks>
/// <para>
/// The wire format itself lives in <see cref="RemoteRequestSignature"/>, shared with the
/// server rather than reimplemented here — the one thing both sides must never disagree
/// about is the bytes they hash.
/// </para>
/// <para>
/// What is decided here is <em>when</em> the body is part of the signature. The server
/// hashes a body only for a non-GET request whose declared length is inside the
/// threshold, and it reads that from <c>Content-Length</c>. So this asks the same
/// question of the same observable: no separate agreement to keep in sync, and a request
/// whose length is unknown (a streamed upload being forwarded) is signed with an empty
/// digest on both sides rather than one side hashing what the other did not.
/// </para>
/// </remarks>
public static class UpstreamRequestSigner
{
    /// <summary>
    /// Whether this request's body is part of what gets signed. Mirrors the server's
    /// rule exactly — see the remarks on the class.
    /// </summary>
    public static bool ShouldHashBody(HttpMethod method, long? contentLength) =>
        method != HttpMethod.Get &&
        method != HttpMethod.Head &&
        contentLength is > 0 and <= RemoteRequestSignature.MaxHashedBodyBytes;

    /// <summary>
    /// Signs <paramref name="request"/> in place. Buffers the body only when it is
    /// actually hashed, so a large or streamed upload is never copied.
    /// </summary>
    public static async Task SignAsync(HttpRequestMessage request, string deviceId, byte[] key,
        long timestampSeconds, string? nonce = null, CancellationToken ct = default)
    {
        var uri = request.RequestUri ??
                  throw new InvalidOperationException("A request must have a URI before it can be signed.");

        var bodyDigest = string.Empty;

        // Content-Length as HttpClient will send it. A content that cannot state its
        // length goes out chunked, and the server then sees no length either — so both
        // sides independently arrive at "not hashed" without having to agree on
        // anything beyond the header itself.
        if (request.Content != null && ShouldHashBody(request.Method, request.Content.Headers.ContentLength))
        {
            // Buffer before reading, or a stream-backed body would be consumed here and
            // the request would go out empty. Bounded by the threshold above.
            await request.Content.LoadIntoBufferAsync(ct);
            var bytes = await request.Content.ReadAsByteArrayAsync(ct);
            bodyDigest = RemoteRequestSignature.HashBody(bytes);
        }

        var canonical = RemoteRequestSignature.BuildCanonicalString(
            deviceId,
            request.Method.Method,
            uri.AbsolutePath,
            // Exactly as it will go on the wire, minus the '?' — re-encoding here is the
            // classic way two implementations end up hashing different strings.
            uri.Query.Length > 1 ? uri.Query[1..] : string.Empty,
            timestampSeconds,
            nonce ??= RemoteRequestSignature.NewNonce(),
            bodyDigest);

        var signature = RemoteRequestSignature.Sign(key, canonical);

        request.Headers.Authorization = AuthenticationHeaderValue.Parse(
            RemoteRequestSignature.BuildHeader(deviceId, timestampSeconds, nonce, signature));
    }
}
