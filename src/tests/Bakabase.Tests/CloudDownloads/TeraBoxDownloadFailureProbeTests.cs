using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Service.Components.CloudDownloads.TeraBox;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class TeraBoxDownloadFailureProbeTests
{
    private const string Link = "https://d.terabox.com/file/0123456789abcdef0123456789abcdef?fid=11-22-33&sign=signed-canary";
    private string _root = null!, _config = null!, _directory = null!, _output = null!;
    private string[] Arguments => ["share-download", "https://www.terabox.com/s/1fixture", _output, "--fsid", "33"];
    private Handler _handler = null!;
    private TeraBoxDownloadFailureProbe _probe = null!;
    private static string NativeOutput(string link = Link, string id = "33", string filename = "file.zip") =>
        "[DEBUG] shorturlinfo response: {\"errno\":0,\"randsk\":\"cookie-canary\",\"access_token\":\"stdout-token-must-not-be-used\"}\n" +
        "[DEBUG] share/download URL params (no token): shareid=11&uk=22&sign=signed-canary&timestamp=123&fid_list=[" + id + "]&sekey=cookie-canary\n" +
        "[DEBUG] share/download response: " + JsonSerializer.Serialize(new {errno = 0, dlink = link, file_info = new {size = 100, filename}}) + "\n";
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "terabox-rejection-tests-" + Guid.NewGuid().ToString("N"));
        _config = Path.Combine(_root, "profile", "terabox", "config.json");
        _directory = Path.Combine(_root, "nas", "stage"); _output = Path.Combine(_directory, "file.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(_config)!); Directory.CreateDirectory(_directory);
        File.WriteAllText(_config, "{\"auth\":{\"access_token\":\"profile-token+canary\"}}");
        _handler = new(); _probe = new(_handler);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private Task<string> Probe(string? output = null, CancellationToken ct = default) =>
        _probe.ClassifyAsync(_config, Arguments, _directory, output ?? NativeOutput(), ct);

    [TestMethod]
    public async Task ExactNative403ReadsKnownBodyUsingOnlyTheBoundProfileToken()
    {
        var stream = _handler.Respond(HttpStatusCode.Forbidden, "{\"error_code\":31045,\"error_msg\":\"user not exists\",\"other\":\"response-token-canary\"}");
        var result = await Probe();
        Assert.AreEqual(TeraBoxDownloadFailureProbe.IdentityRejected, result);
        Assert.AreEqual(1, _handler.Calls);
        Assert.AreEqual("d.terabox.com", _handler.Uri!.Host);
        StringAssert.Contains(_handler.Uri.Query, "access_token=profile-token%2Bcanary");
        Assert.IsFalse(_handler.Uri.Query.Contains("stdout-token"));
        Assert.AreEqual("bytes=0-0", _handler.Range);
        Assert.AreEqual("terabox-storage/1.0", _handler.UserAgent);
        Assert.IsTrue(stream.Disposed);
        Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
        Assert.IsFalse(result.Contains("canary"));
    }

    [TestMethod]
    public void OnlyTheExactSingleFileNativeDownloadErrorActivatesInspection()
    {
        Assert.IsTrue(TeraBoxDownloadFailureProbe.IsNativeDownloadDenied(Arguments, "Error: download failed: HTTP 403\nUsage:\n"));
        foreach (var error in new[] {"Error: download failed: HTTP 401", "Error: Failed to get share info: download failed: HTTP 403",
                     "prefix Error: download failed: HTTP 403", "Error: download failed: HTTP 403 token-canary"})
            Assert.IsFalse(TeraBoxDownloadFailureProbe.IsNativeDownloadDenied(Arguments, error));
        Assert.IsFalse(TeraBoxDownloadFailureProbe.IsNativeDownloadDenied(["share-info", Arguments[1]], "Error: download failed: HTTP 403"));
        Assert.IsFalse(TeraBoxDownloadFailureProbe.IsNativeDownloadDenied(Arguments.Take(3).ToArray(), "Error: download failed: HTTP 403"));
    }

    [TestMethod]
    public async Task PartialEvenEmptyLocalFilesPreventAnyAdditionalRequest()
    {
        await File.WriteAllTextAsync(_output, "");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        Assert.AreEqual(0, _handler.Calls);
        await File.WriteAllTextAsync(_output, "partial-must-survive");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        Assert.AreEqual("partial-must-survive", await File.ReadAllTextAsync(_output));
        Assert.AreEqual(0, _handler.Calls);
    }

    [TestMethod]
    public async Task SuccessIsNotADownloadFallbackAndReadsAtMostOneByte()
    {
        var stream = _handler.Respond(HttpStatusCode.OK, new string('x', 10000), "application/octet-stream");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        Assert.AreEqual(1, stream.BytesRead);
        Assert.IsTrue(stream.Disposed);
        Assert.IsFalse(File.Exists(_output));
    }

    [TestMethod]
    public async Task RedirectNeverReceivesCredentialsAndItsBodyIsNotRead()
    {
        var stream = _handler.Respond(HttpStatusCode.Redirect, "token-canary");
        _handler.Response!.Headers.Location = new Uri("https://unknown.invalid/credential-target");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        Assert.AreEqual(1, _handler.Calls); Assert.AreEqual(0, stream.BytesRead);
        using var handler = TeraBoxDownloadFailureProbe.CreateHandler(new BakabaseWebProxy(null!));
        Assert.IsFalse(handler.AllowAutoRedirect); Assert.IsFalse(handler.UseCookies); Assert.IsTrue(handler.UseProxy);
        Assert.AreEqual(0, handler.MaxResponseDrainSize); Assert.AreEqual(TimeSpan.Zero, handler.ResponseDrainTimeout);
    }

    [TestMethod]
    public async Task OversizedUnknownAndUntrustedBodiesStayBoundedAndPrivate()
    {
        foreach (var body in new[] {"{\"error_code\":\"31045\",\"error_msg\":\"token-canary\"}",
                     "{\"error_code\":112,\"error_msg\":\"token-canary\"}", "arbitrary token-canary body", "{\"error_code\":31045} trailing"})
        {
            _handler.Respond(HttpStatusCode.Forbidden, body);
            Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        }
        var stream = _handler.Respond(HttpStatusCode.Forbidden, new string(' ', 5000) + "{\"error_code\":31045}");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe()); Assert.AreEqual(4096, stream.BytesRead);
        stream = _handler.Respond(HttpStatusCode.Forbidden, "{\"error_code\":31045}", "text/html");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe()); Assert.AreEqual(0, stream.BytesRead);
        stream = _handler.Respond(HttpStatusCode.Forbidden, "{\"error_code\":31045}");
        _handler.Response!.Content.Headers.ContentLength = 5000;
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe()); Assert.AreEqual(0, stream.BytesRead);
    }

    [TestMethod]
    public async Task WrongGrantScopeProtocolAndUnknownStdoutNeverSendToken()
    {
        foreach (var text in new[] {
                     NativeOutput(Link.Replace("d.terabox.com", "unknown.invalid")), NativeOutput(Link.Replace("https:", "http:")),
                     NativeOutput(Link.Replace("11-22-33", "11-22-34")), NativeOutput(Link + "&ACCESS_TOKEN=attacker"),
                     NativeOutput(Link + "&fid=11-22-33"), NativeOutput(Link + "#fragment"),
                     NativeOutput(Link.Replace("d.terabox.com", "user:pass@d.terabox.com")), NativeOutput(id: "34"),
                     NativeOutput(filename: "another.zip"), NativeOutput() + "{}", NativeOutput() + NativeOutput(),
                     "[DEBUG] unknown: token-canary\n" + NativeOutput(), NativeOutput().TrimEnd()})
            Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe(text));
        Assert.AreEqual(0, _handler.Calls);
    }

    [TestMethod]
    public async Task NetworkExceptionCannotLeakSignedUrlAndUserCancellationStillPropagates()
    {
        _handler.Error = new HttpRequestException("https://d.terabox.com/file/signed?access_token=token-canary");
        Assert.AreEqual(TeraBoxDownloadFailureProbe.AccessDenied, await Probe());
        _handler.Error = null;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Probe(ct: cancelled.Token));
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal Uri? Uri;
        internal string? Range, UserAgent;
        internal Exception? Error;
        internal HttpResponseMessage? Response;
        internal CountingStream Respond(HttpStatusCode status, string body, string contentType = "text/plain")
        {
            var stream = new CountingStream(Encoding.UTF8.GetBytes(body));
            Response = new(status) {Content = new StreamContent(stream)};
            Response.Content.Headers.ContentType = new(contentType); return stream;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Uri = request.RequestUri; Range = request.Headers.Range?.ToString(); UserAgent = request.Headers.UserAgent.ToString();
            if (Error != null) return Task.FromException<HttpResponseMessage>(Error);
            return Task.FromResult(Response ?? new(HttpStatusCode.Forbidden) {Content = new StringContent("Unknown")});
        }
    }
    private sealed class CountingStream(byte[] bytes) : Stream
    {
        internal int BytesRead; internal bool Disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { var length = Math.Min(count, bytes.Length - BytesRead); bytes.AsSpan(BytesRead, length).CopyTo(buffer.AsSpan(offset, length)); BytesRead += length; return length; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); var length = Math.Min(buffer.Length, bytes.Length - BytesRead); bytes.AsMemory(BytesRead, length).CopyTo(buffer); BytesRead += length; return ValueTask.FromResult(length); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
