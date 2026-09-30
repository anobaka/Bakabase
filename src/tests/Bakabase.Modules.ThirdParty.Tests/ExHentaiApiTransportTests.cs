using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiApiTransportTests
{
    private const int FirstId = 2000001;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(25)]
    public async Task GalleryMetadataPostHasAnExactContentLengthAndAnUnchunkedJsonBodyOnTheWire(int galleryCount)
    {
        // JsonContent has an unknown length until it is buffered. A fixture that reads
        // request.Content before forwarding it would conceal the HTTP/1.1 regression.
        // This transport changes only the destination, then uses real sockets unchanged.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var capture = CaptureRequestAsync(listener, galleryCount, deadline.Token);
        try
        {
            var endpoint = (IPEndPoint) listener.LocalEndpoint;
            using var transport = new SocketsHttpHandler {UseProxy = false, AllowAutoRedirect = false};
            using var redirect = new LoopbackTransport(new Uri($"http://127.0.0.1:{endpoint.Port}/"), transport);
            using var http = new HttpClient(redirect) {Timeout = TimeSpan.FromSeconds(30)};
            var client = new ExHentaiClient(new Factory(http), NullLoggerFactory.Instance);
            var urls = Enumerable.Range(FirstId, galleryCount)
                .Select(id => $"https://exhentai.org/g/{id}/{Token(id)}/").ToArray();

            var resources = await client.GetGalleryMetadata(urls, deadline.Token);
            var request = await capture;

            Assert.AreEqual("POST /api.php HTTP/1.1", request.RequestLine);
            Assert.IsFalse(request.Headers.ContainsKey("Transfer-Encoding"),
                "The legacy API does not read chunked JSON bodies; inspect the actual socket framing.");
            Assert.IsTrue(request.Headers.TryGetValue("Content-Length", out var lengthHeader),
                "The metadata request must declare its byte length before sending.");
            Assert.IsTrue(int.TryParse(lengthHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredLength));
            Assert.AreEqual(request.Body.Length, declaredLength);
            StringAssert.StartsWith(request.Headers["Content-Type"], "application/json");
            var json = StrictUtf8.GetString(request.Body);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(json), declaredLength);
            using var body = JsonDocument.Parse(request.Body);
            Assert.AreEqual("gdata", body.RootElement.GetProperty("method").GetString());
            Assert.AreEqual(1, body.RootElement.GetProperty("namespace").GetInt32());
            var keys = body.RootElement.GetProperty("gidlist");
            Assert.AreEqual(galleryCount, keys.GetArrayLength());
            for (var index = 0; index < galleryCount; index++)
            {
                var id = FirstId + index;
                Assert.AreEqual(id, keys[index][0].GetInt32());
                Assert.AreEqual(Token(id), keys[index][1].GetString());
                Assert.AreEqual(id, resources[index].Id);
                Assert.AreEqual("图库 " + id, resources[index].Name);
            }
        }
        finally
        {
            await deadline.CancelAsync();
            listener.Stop();
            // Observe a pending accept/read if a client-side assertion or request failed.
            try { await capture; }
            catch (Exception) when (deadline.IsCancellationRequested) { }
        }
    }

    private static string Token(int id) => id.ToString("x10", CultureInfo.InvariantCulture);

    private static async Task<WireRequest> CaptureRequestAsync(TcpListener listener, int galleryCount,
        CancellationToken ct)
    {
        using var peer = await listener.AcceptTcpClientAsync(ct);
        await using var stream = peer.GetStream();
        var headerBlock = await ReadHeadersAsync(stream, ct);
        var lines = Encoding.ASCII.GetString(headerBlock).Split("\r\n", StringSplitOptions.None);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0) throw new InvalidDataException("Invalid HTTP request header.");
            headers.Add(line[..separator], line[(separator + 1)..].Trim());
        }
        var body = await ReadBodyAsync(stream, headers, ct);
        // Read old chunked requests too, then return a valid response: the regression must
        // fail the wire-header assertion, rather than merely timing out in the test server.
        var response = JsonSerializer.SerializeToUtf8Bytes(new
        {
            gmetadata = Enumerable.Range(FirstId, galleryCount).Select(id => new
            {
                gid = id, token = Token(id), title = "图库 " + id, category = "Manga",
                posted = 1700000000, filecount = 1, rating = 4.5, torrentcount = 0,
                tags = Array.Empty<string>()
            })
        });
        var responseHeaders = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(responseHeaders, ct);
        await stream.WriteAsync(response, ct);
        return new WireRequest(lines[0], headers, body);
    }

    private static async Task<byte[]> ReadHeadersAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var one = new byte[1];
        uint suffix = 0;
        while (output.Length < 16 * 1024)
        {
            await stream.ReadExactlyAsync(one, ct);
            output.WriteByte(one[0]);
            suffix = (suffix << 8) | one[0];
            if (suffix == 0x0d0a0d0a) return output.ToArray();
        }
        throw new InvalidDataException("The local HTTP request headers exceeded the test limit.");
    }

    private static async Task<byte[]> ReadBodyAsync(Stream stream, IReadOnlyDictionary<string, string> headers,
        CancellationToken ct)
    {
        const int maximum = 64 * 1024;
        if (headers.TryGetValue("Content-Length", out var rawLength))
        {
            var length = int.Parse(rawLength, NumberStyles.None, CultureInfo.InvariantCulture);
            if (length < 0 || length > maximum) throw new InvalidDataException("The local JSON body is too large.");
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, ct);
            return body;
        }
        if (!headers.TryGetValue("Transfer-Encoding", out var encoding) ||
            !encoding.Equals("chunked", StringComparison.OrdinalIgnoreCase)) return [];

        using var chunks = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(stream, ct);
            var size = int.Parse(sizeLine.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (size < 0 || chunks.Length + size > maximum)
                throw new InvalidDataException("The local chunked JSON body is too large.");
            if (size == 0)
            {
                while ((await ReadLineAsync(stream, ct)).Length > 0) { }
                return chunks.ToArray();
            }
            var chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk, ct);
            chunks.Write(chunk);
            if ((await ReadLineAsync(stream, ct)).Length != 0)
                throw new InvalidDataException("A chunk did not end in CRLF.");
        }
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var one = new byte[1];
        while (output.Length < 1024)
        {
            await stream.ReadExactlyAsync(one, ct);
            output.WriteByte(one[0]);
            if (one[0] != '\n') continue;
            var bytes = output.ToArray();
            if (bytes.Length < 2 || bytes[^2] != '\r') throw new InvalidDataException("Invalid HTTP line ending.");
            return Encoding.ASCII.GetString(bytes, 0, bytes.Length - 2);
        }
        throw new InvalidDataException("The local HTTP chunk line exceeded the test limit.");
    }

    private sealed record WireRequest(string RequestLine, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class LoopbackTransport(Uri address, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.AreEqual(ExHentaiClient.ApiUrl, request.RequestUri!.AbsoluteUri);
            request.RequestUri = new Uri(address, request.RequestUri.PathAndQuery);
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            // Do not access request.Content or its headers: even reading it in a fixture
            // can buffer JsonContent and change what SocketsHttpHandler sends.
            return base.SendAsync(request, ct);
        }
    }
}
