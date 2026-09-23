using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Queries;

[TestClass]
public sealed class HttpPeerSearchClientTests
{
    private static PeerSessionSnapshot Session() => new("node", "http://127.0.0.1:34567",
        new NodeCredentials("grant", "self", "node", "epoch", "unused-test-key", 1), TimeSpan.Zero,
        new NodeInfo("node", "epoch", "Owner", 1, DateTimeOffset.UnixEpoch), DateTimeOffset.UnixEpoch);

    [TestMethod]
    public async Task ReadsActualRawSerializerBytesWithOnePinnedPeerSession()
    {
        var block = new NodeQueryBlock
            { NodeId = "node", LibraryEpoch = "epoch", SnapshotId = "snapshot", QueryHash = "hash", ExpiresInMs = 1000 };
        var transport = new Transport(() => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(block, FederationJson.Options), Encoding.UTF8, "application/json")
        });
        var session = Session();
        var client = new HttpPeerSearchClient(transport, session);
        var result = await client.CreateAsync(new() { ExpectedLibraryEpoch = "epoch" }, default);
        Assert.AreEqual("snapshot", result.SnapshotId);
        Assert.AreSame(session, transport.ObservedSession);
        Assert.AreEqual("/federation/v1/export/queries", transport.Path);
    }

    [TestMethod]
    public async Task PreservesStructuredPermissionFailuresAndRejectsAnUnexpectedEnvelope()
    {
        var denied = new HttpPeerSearchClient(new Transport(() => new(HttpStatusCode.Forbidden)
            { Content = new StringContent("{\"code\":\"SharingDisabled\",\"retryable\":false}") }), Session());
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => denied.ValidateAsync("snapshot", default));
        Assert.AreEqual("SharingDisabled", error.Code);
        Assert.AreEqual(403, error.StatusCode);
        var wrapped = new HttpPeerSearchClient(new Transport(() => new(HttpStatusCode.OK)
            { Content = new StringContent("{\"data\":{\"snapshotId\":\"old-envelope\"}}") }), Session());
        var invalid = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => wrapped.CreateAsync(new(), default));
        Assert.AreEqual("InvalidPeerResponse", invalid.Code);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("\"upstream unavailable\"")]
    [DataRow("<html>upstream unavailable</html>")]
    public async Task PreservesHttpFailureForNonObjectProxyErrorBodies(string body)
    {
        var client = new HttpPeerSearchClient(new Transport(() => new(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent(body) }), Session());
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => client.ValidateAsync("snapshot", default));
        Assert.AreEqual("PeerUnavailable", error.Code);
        Assert.AreEqual(503, error.StatusCode);
        Assert.IsTrue(error.Retryable);
    }

    [TestMethod]
    public async Task LimitsDecodedStreamEvenWithoutContentLength()
    {
        var body = new UnknownLengthStream();
        var client = new HttpPeerSearchClient(new Transport(() => new(HttpStatusCode.OK)
            { Content = new StreamContent(body) }), Session());
        var error = await Assert.ThrowsExceptionAsync<FederationQueryException>(() => client.CreateAsync(new(), default));
        Assert.AreEqual("InvalidPeerResponse", error.Code);
        Assert.IsTrue(body.BytesRead <= 8 * 1024 * 1024 + 16 * 1024);
        Assert.IsTrue(body.Disposed);
    }

    private sealed class Transport(Func<HttpResponseMessage> respond) : INodeTransport
    {
        public PeerSessionSnapshot? ObservedSession;
        public string? Path;
        public Task<HttpResponseMessage> SendAsync(string nodeId, HttpMethod method, string relativePath,
            object? body = null, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Queries must pin their verified target instead of resolving it for each block.");
        public Task<HttpResponseMessage> SendAsync(PeerSessionSnapshot session, HttpMethod method, string relativePath,
            object? body = null, CancellationToken cancellationToken = default, IReadOnlyDictionary<string, string>? headers = null)
        {
            ObservedSession = session;
            Path = relativePath;
            return Task.FromResult(respond());
        }
    }

    private sealed class UnknownLengthStream : Stream
    {
        public int BytesRead;
        public bool Disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)' ');
            BytesRead += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Span.Fill((byte)' ');
            BytesRead += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
