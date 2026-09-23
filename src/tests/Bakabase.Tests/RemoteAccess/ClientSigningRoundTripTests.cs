using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Runs what the client signs straight into what the server verifies.
/// </summary>
/// <remarks>
/// Both halves already have their own tests, and both can be right on their own while
/// disagreeing with each other — over how a query is encoded, or over when a body counts.
/// The only way to know they agree is to run one against the other, so these tests
/// extract from the outgoing <see cref="HttpRequestMessage"/> exactly what the server's
/// middleware would pull off the wire, and hand that to the real authenticator.
/// </remarks>
[TestClass]
public class ClientSigningRoundTripTests
{
    private string _root = null!;
    private DateTime _now;
    private RemoteDeviceService _devices = null!;
    private RemoteDeviceAuthenticator _auth = null!;
    private PairingCredentials _device = null!;
    private byte[] _key = null!;

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-roundtrip-tests", Guid.NewGuid().ToString("N"));
        _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var store = new RemoteDeviceStore(new TempDirectory(Path.Combine(_root, "remote-access")));
        _devices = new RemoteDeviceService(store, () => _now);
        _auth = new RemoteDeviceAuthenticator(_devices, new NonceCache(TimeSpan.FromMinutes(10), 4096, () => _now),
            () => _now);

        var issue = await _devices.IssuePairingCodeAsync();
        _device = (await _devices.PairWithCodeAsync(issue.Code, "Laptop", RemoteDevicePlatform.Windows)).Credentials!;
        _key = RemoteRequestSignature.FromBase64Url(_device.Key);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    /// <summary>
    /// Signs the request, then reads it back the way the server middleware does — the
    /// header, the method, the path, the raw query, and a body digest computed under the
    /// server's own rule rather than the client's.
    /// </summary>
    private async Task<DeviceAuthResult> RoundTrip(HttpRequestMessage request, long? timestamp = null)
    {
        await UpstreamRequestSigner.SignAsync(request, _device.DeviceId, _key,
            timestamp ?? new DateTimeOffset(_now).ToUnixTimeSeconds());

        var uri = request.RequestUri!;
        var bodyDigest = string.Empty;

        // The server's condition, written out here rather than reused from the client,
        // so a change to one side does not silently pass by agreeing with itself.
        var length = request.Content?.Headers.ContentLength;
        if (request.Content != null &&
            request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
            length is > 0 and <= RemoteRequestSignature.MaxHashedBodyBytes)
        {
            bodyDigest = RemoteRequestSignature.HashBody(await request.Content.ReadAsByteArrayAsync());
        }

        return _auth.Authenticate(request.Headers.Authorization?.ToString(), request.Method.Method,
            uri.AbsolutePath, uri.Query.Length > 1 ? uri.Query[1..] : string.Empty, bodyDigest);
    }

    [TestMethod]
    public async Task A_plain_get_round_trips()
    {
        var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
            "http://192.168.1.5:34567/resource/search"));

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, result.Outcome);
        Assert.AreEqual(_device.DeviceId, result.Device!.Id);
    }

    [TestMethod]
    public async Task A_query_carrying_an_encoded_path_round_trips()
    {
        // /file/raw carries a whole filesystem path in the query. Re-encoding it on
        // either side is the classic way the two implementations end up hashing
        // different strings, so this is the case worth pinning.
        var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
            "http://192.168.1.5:34567/file/raw?fullname=D%3A%5CMedia%5CShow%20%5B2024%5D%5Cep01.mkv"));

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, result.Outcome);
    }

    [TestMethod]
    public async Task Repeated_query_keys_round_trip()
    {
        // Both Dio and HttpClient write a list as ids=1&ids=2 rather than ids[]=…, and
        // nothing in the chain may normalise that away.
        var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
            "http://192.168.1.5:34567/resource/keys?ids=1&ids=2&ids=3"));

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, result.Outcome);
    }

    [TestMethod]
    public async Task A_json_body_round_trips_and_is_covered()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://192.168.1.5:34567/resource/search")
        {
            Content = new StringContent("{\"keyword\":\"a\"}", Encoding.UTF8, "application/json")
        };

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, (await RoundTrip(request)).Outcome);

        // And the body is genuinely part of it: the same signature against a different
        // body must not verify.
        var tampered = _auth.Authenticate(request.Headers.Authorization?.ToString(), "POST", "/resource/search", "",
            RemoteRequestSignature.HashBody("{\"keyword\":\"b\"}"u8));

        Assert.AreEqual(DeviceAuthOutcome.BadSignature, tampered.Outcome);
    }

    [TestMethod]
    public async Task An_empty_body_is_treated_the_same_by_both_sides()
    {
        // Content-Length 0 falls outside the "> 0" the server tests for, so neither side
        // hashes it. If only one side skipped, every parameterless POST would 401.
        var request = new HttpRequestMessage(HttpMethod.Post,
            "http://192.168.1.5:34567/remote-access/pairing/code")
        {
            Content = new StringContent(string.Empty)
        };

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, (await RoundTrip(request)).Outcome);
    }

    [TestMethod]
    public async Task A_streamed_body_of_unknown_length_is_signed_without_it()
    {
        // A forwarded upload goes out chunked, so the server sees no Content-Length and
        // does not hash. The client has to reach the same conclusion from the same
        // observable, or every upload through the forwarding layer would be refused.
        var content = new StreamContent(new NonSeekableStream("payload"u8.ToArray()));
        var request = new HttpRequestMessage(HttpMethod.Post, "http://192.168.1.5:34567/file/upload")
        {
            Content = content
        };

        Assert.IsNull(content.Headers.ContentLength);
        Assert.AreEqual(DeviceAuthOutcome.Authenticated, (await RoundTrip(request)).Outcome);
    }

    [TestMethod]
    public async Task A_hashed_body_survives_being_hashed()
    {
        // Reading the content to hash it must not consume it, or the request would go
        // out empty after being signed for a body it no longer carries.
        var request = new HttpRequestMessage(HttpMethod.Post, "http://192.168.1.5:34567/resource/search")
        {
            Content = new StreamContent(new MemoryStream("{\"keyword\":\"a\"}"u8.ToArray()))
        };

        await UpstreamRequestSigner.SignAsync(request, _device.DeviceId, _key,
            new DateTimeOffset(_now).ToUnixTimeSeconds());

        Assert.AreEqual("{\"keyword\":\"a\"}", await request.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task A_signature_the_client_produced_works_exactly_once()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.5:34567/resource/search");
        Assert.AreEqual(DeviceAuthOutcome.Authenticated, (await RoundTrip(request)).Outcome);

        var replay = _auth.Authenticate(request.Headers.Authorization?.ToString(), "GET", "/resource/search", "", "");
        Assert.AreEqual(DeviceAuthOutcome.Replayed, replay.Outcome);
    }

    [TestMethod]
    public async Task Two_requests_in_the_same_second_do_not_collide()
    {
        // The nonce is what separates them; a client that reused one would find its
        // second request of any given second refused as a replay.
        for (var i = 0; i < 5; i++)
        {
            var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
                "http://192.168.1.5:34567/resource/search"));

            Assert.AreEqual(DeviceAuthOutcome.Authenticated, result.Outcome, $"request {i}");
        }
    }

    [TestMethod]
    public async Task A_clock_the_server_disagrees_with_is_reported_as_such()
    {
        var stale = new DateTimeOffset(_now.AddMinutes(-6)).ToUnixTimeSeconds();
        var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
            "http://192.168.1.5:34567/resource/search"), stale);

        Assert.AreEqual(DeviceAuthOutcome.Expired, result.Outcome);
    }

    [TestMethod]
    public async Task The_server_clock_offset_brings_a_wrong_clock_back_into_range()
    {
        // A machine six minutes behind would have every request refused. Synchronising
        // against what the handshake reported is what makes it work anyway.
        var localNow = _now.AddMinutes(-6);
        var clock = new ServerClock(() => localNow);
        clock.Synchronize(serverTime: _now, requestSentAt: localNow);

        var result = await RoundTrip(new HttpRequestMessage(HttpMethod.Get,
            "http://192.168.1.5:34567/resource/search"), clock.NowUnixSeconds);

        Assert.AreEqual(DeviceAuthOutcome.Authenticated, result.Outcome);
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
