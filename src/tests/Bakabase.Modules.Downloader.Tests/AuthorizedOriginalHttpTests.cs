using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class AuthorizedOriginalHttpTests
{
    private const int Segment = AuthorizedOriginalCheckpointStore.SegmentBytes;
    private const string ClientName = "test-official-cdn";
    private const string Secret = "synthetic-signed-placeholder";
    private const string Tag = "\"synthetic-representation\"";
    private readonly byte[] _original = RandomNumberGenerator.GetBytes(Segment + 317);
    private string _root = null!;
    private string DirectoryPath => Path.Combine(_root, "member");
    private readonly AuthorizedOriginalIdentity _identity = new("test", "operation", "account-generation", "folder", "member", "immutable", Segment + 317, "attempt");
    private int _resolved;
    private Task<Uri> Resolve(CancellationToken _) =>
        Task.FromResult(new Uri($"https://cdn.example.org/original?sig={Secret}-{++_resolved}"));

    [TestInitialize] public void Setup() => _root = Path.Combine(Path.GetTempPath(), "BakabaseOriginal_" + Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private ServiceProvider Services(Func<HttpRequestMessage, HttpResponseMessage> handler, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection().AddDownloader(_ => _root);
        if (logs != null) services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddHttpClient(ClientName).ForAuthorizedOriginalDownloads()
            .ConfigurePrimaryHttpMessageHandler(() => new Handler(handler));
        return services.BuildServiceProvider();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(callback(request));
    }

    private HttpResponseMessage Reply(HttpRequestMessage request, string? tag = Tag)
    {
        var range = request.Headers.Range!.Ranges.Single();
        var start = checked((int)range.From!.Value);
        var end = checked((int)range.To!.Value);
        Assert.IsTrue(request.Headers.AcceptEncoding.Any(a => a.Value == "identity"));
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {Content = new ByteArrayContent(_original, start, end - start + 1)};
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _original.Length);
        if (tag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(tag);
        return response;
    }

    private Task<string> Download(IAuthorizedOriginalHttpDownloader downloader) =>
        downloader.DownloadAsync(_identity, DirectoryPath, ClientName, Resolve, null, CancellationToken.None);

    private async Task<AuthorizedOriginalCheckpoint> InterruptAfterOneSegment()
    {
        using var services = Services(req => req.Headers.Range!.Ranges.Single().From == Segment
            ? throw new HttpRequestException("transient " + Secret) : Reply(req));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var failure = await Assert.ThrowsExactlyAsync<AuthorizedOriginalDownloadException>(() => Download(downloader));
        Assert.AreEqual("ORIGINAL_HTTP_TRANSFER_FAILED", failure.Code);
        Assert.IsFalse(failure.ToString().Contains(Secret));
        var result = await downloader.InspectAsync(_identity, DirectoryPath, default);
        Assert.AreEqual(new AuthorizedOriginalCheckpoint(true, Segment, false), result);
        return result;
    }

    [TestMethod]
    public async Task Reopened_transfer_skips_verified_segment_rotates_expired_address_and_proves_handoff()
    {
        await InterruptAfterOneSegment();
        var ranges = new List<(long Start, long End, string Query)>();
        var expired = false;
        using var services = Services(req =>
        {
            var range = req.Headers.Range!.Ranges.Single();
            ranges.Add((range.From!.Value, range.To!.Value, req.RequestUri!.Query));
            if (range.From == Segment && !expired)
            {
                expired = true;
                return new(HttpStatusCode.Forbidden);
            }
            if (range.From == Segment || ranges.Count > 2)
            {
                Assert.AreEqual(Tag, req.Headers.IfRange!.EntityTag!.ToString());
                Assert.AreEqual(Tag, req.Headers.IfMatch.Single().ToString());
            }
            return Reply(req);
        });
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var path = await Download(downloader);
        CollectionAssert.AreEqual(_original, await File.ReadAllBytesAsync(path));
        CollectionAssert.AreEqual(new long[] {0, Segment, Segment, 0}, ranges.Select(r => r.Start).ToArray());
        Assert.AreNotEqual(ranges[1].Query, ranges[2].Query);
        Assert.AreNotEqual(ranges[2].Query, ranges[3].Query);
        var ledger = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(DirectoryPath, "checkpoint")));
        foreach (var value in new[] {Secret, "https://", Tag, _identity.OperationId, _identity.MemberId, _identity.AccountVersion})
            Assert.IsFalse(ledger.Contains(value));
        Assert.IsTrue((await downloader.InspectAsync(_identity, DirectoryPath, default)).IsComplete);
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectHandedOffAsync(_identity, DirectoryPath, default));
        File.Move(path, Path.Combine(_root, "provider-verified-final"));
        Assert.AreEqual(new AuthorizedOriginalCheckpoint(true, _original.Length, true),
            await downloader.InspectHandedOffAsync(_identity, DirectoryPath, default));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectAsync(_identity, DirectoryPath, default));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectHandedOffAsync(_identity with {AccountVersion = "different"}, DirectoryPath, default));
    }

    [TestMethod]
    public async Task Changed_identity_representation_or_saved_bytes_cannot_contribute_to_new_payload()
    {
        await InterruptAfterOneSegment();
        var dataRequests = 0;
        var tag = "\"replacement\"";
        using var services = Services(req =>
        {
            if (req.Headers.Range!.Ranges.Single().To != 0) dataRequests++;
            return Reply(req, tag);
        });
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        foreach (var identity in new[] {_identity with {OperationId = "other"}, _identity with {AttemptScope = "other"},
            _identity with {ImmutableId = "changed"}, _identity with {Length = 112}})
            await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectAsync(identity, DirectoryPath, default));
        await Expect("ORIGINAL_HTTP_SOURCE_CHANGED", () => Download(downloader));
        tag = Tag;
        using (var file = File.OpenWrite(Path.Combine(DirectoryPath, "payload.download")))
            file.WriteByte((byte)(_original[0] ^ 0xff));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => Download(downloader));
        Assert.AreEqual(0, dataRequests);
        Assert.IsFalse(File.Exists(Path.Combine(DirectoryPath, "payload")));
    }

    [TestMethod]
    [DataRow("ignored", "ORIGINAL_HTTP_RESUME_UNSUPPORTED")]
    [DataRow("weak", "ORIGINAL_HTTP_RESUME_UNSUPPORTED")]
    [DataRow("missing", "ORIGINAL_HTTP_RESUME_UNSUPPORTED")]
    [DataRow("offset", "ORIGINAL_HTTP_RANGE_INVALID")]
    [DataRow("encoded", "ORIGINAL_HTTP_RANGE_INVALID")]
    [DataRow("short", "ORIGINAL_HTTP_TRANSFER_FAILED")]
    [DataRow("overlong", "ORIGINAL_HTTP_RANGE_INVALID")]
    [DataRow("changedLength", "ORIGINAL_HTTP_SOURCE_CHANGED")]
    [DataRow("changedAtFinish", "ORIGINAL_HTTP_SOURCE_CHANGED")]
    [DataRow("missingRangeTag", "ORIGINAL_HTTP_SOURCE_CHANGED")]
    public async Task Unsafe_responses_never_publish_a_file(string mode, string failure)
    {
        var requests = 0;
        using var services = Services(req =>
        {
            requests++;
            var probe = req.Headers.Range!.Ranges.Single().To == 0;
            if (mode == "ignored") return new(HttpStatusCode.OK);
            var tag = mode switch { "weak" => "W/" + Tag, "missing" => null, "missingRangeTag" when !probe => null,
                "changedAtFinish" when requests > 1 && probe => "\"new\"", _ => Tag };
            var response = Reply(req, tag);
            var range = response.Content.Headers.ContentRange!;
            if (mode == "offset" && !probe)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(range.From!.Value + 1, range.To!.Value, _original.Length);
            if (mode == "changedLength")
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, _original.Length + 2);
            if (mode == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
            if (mode is "short" or "overlong" && !probe)
            {
                response.Content = new ByteArrayContent(new byte[mode == "short" ? 1 : Segment + 1]);
                response.Content.Headers.ContentRange = range;
                response.Content.Headers.ContentLength = Segment;
            }
            return response;
        });
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        await Expect(failure, () => Download(downloader));
        Assert.IsFalse(File.Exists(Path.Combine(DirectoryPath, "payload")));
    }

    [TestMethod]
    public async Task No_other_writer_is_admitted_and_preview_does_not_touch_an_unstarted_scope()
    {
        using var services = Services(req => Reply(req));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        Assert.IsFalse((await downloader.InspectAsync(_identity, DirectoryPath, default)).Exists);
        Assert.IsFalse(Directory.Exists(DirectoryPath));
        await Expect("ORIGINAL_HTTP_LOCATION_UNAVAILABLE", () => downloader.DownloadAsync(_identity, DirectoryPath,
            "unregistered", Resolve, null, default));
        Assert.IsFalse(Directory.Exists(DirectoryPath));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = downloader.DownloadAsync(_identity, DirectoryPath, ClientName, async ct =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return await Resolve(ct);
        }, null, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Expect("ORIGINAL_HTTP_BUSY", () => Download(downloader));
            await Expect("ORIGINAL_HTTP_BUSY", () => downloader.InspectAsync(_identity, DirectoryPath, default));
        }
        finally { release.SetResult(); await running; }
    }

    [TestMethod]
    public async Task Foreign_entries_and_links_are_never_modified_or_used_as_journals()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks may require elevated privileges.
        Directory.CreateDirectory(DirectoryPath);
        var outside = Path.Combine(_root, "outside");
        await File.WriteAllTextAsync(outside, "keep");
        var partial = Path.Combine(DirectoryPath, "payload.download");
        File.CreateSymbolicLink(partial, outside);
        using var services = Services(_ => throw new AssertFailedException("Unexpected network operation."));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => Download(downloader));
        File.Delete(partial);
        var unknown = Path.Combine(DirectoryPath, "ordinary-file");
        await File.WriteAllTextAsync(unknown, "keep");
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectAsync(_identity, DirectoryPath, default));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(outside));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(unknown));
    }

    [DllImport("libc", EntryPoint = "link")]
    private static extern int HardLink(string target, string path);
    [DllImport("libc", EntryPoint = "mkfifo")]
    private static extern int CreatePipe(string path, uint mode);

    [TestMethod]
    public async Task Linked_or_special_private_files_are_rejected_before_opening_or_truncating()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        await InterruptAfterOneSegment();
        var partial = Path.Combine(DirectoryPath, "payload.download");
        var separate = Path.Combine(_root, "user-file");
        File.Move(partial, separate);
        Assert.AreEqual(0, HardLink(separate, partial));
        using var services = Services(_ => throw new AssertFailedException("Unexpected network operation."));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => Download(downloader));
        File.Delete(partial);
        Assert.AreEqual(0, CreatePipe(partial, 0x180));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectAsync(_identity, DirectoryPath, default));
        Assert.AreEqual(Segment, new FileInfo(separate).Length);
    }

    [TestMethod]
    public async Task Unacknowledged_tail_and_torn_digest_are_fetched_again_after_restart()
    {
        await InterruptAfterOneSegment();
        var partial = Path.Combine(DirectoryPath, "payload.download");
        await using (var append = new FileStream(partial, FileMode.Append))
            await append.WriteAsync(new byte[149]);
        await using (var append = new FileStream(Path.Combine(DirectoryPath, "checkpoint"), FileMode.Append))
            await append.WriteAsync(new byte[11]);
        using var services = Services(req => Reply(req));
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        Assert.AreEqual(Segment, (await downloader.InspectAsync(_identity, DirectoryPath, default)).VerifiedBytes);
        CollectionAssert.AreEqual(_original, await File.ReadAllBytesAsync(await Download(downloader)));
    }

    [TestMethod]
    public async Task Resolver_failures_and_untrusted_addresses_never_reach_the_named_client_or_diagnostics()
    {
        var calls = 0;
        using var services = Services(req => { calls++; return Reply(req); });
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        foreach (var resolver in new Func<CancellationToken, Task<Uri>>[]
        {
            _ => throw new InvalidOperationException("untrusted resolver message " + Secret),
            _ => Task.FromResult(new Uri("https://127.0.0.1/?secret=" + Secret)),
            _ => Task.FromResult(new Uri("http://cdn.example.org/?secret=" + Secret)),
            _ => Task.FromResult(new Uri("https://cdn.example.org/original#" + Secret))
        })
        {
            var error = await Assert.ThrowsExactlyAsync<AuthorizedOriginalDownloadException>(() =>
                downloader.DownloadAsync(_identity, DirectoryPath, ClientName, resolver, null, default));
            Assert.AreEqual("ORIGINAL_HTTP_LOCATION_UNAVAILABLE", error.Code);
            Assert.IsFalse(error.ToString().Contains(Secret));
        }
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task Verified_empty_original_uses_its_own_zero_length_representation_and_handoff_seal()
    {
        var empty = _identity with {Length = 0};
        var count = 0;
        using var services = Services(req =>
        {
            count++;
            Assert.AreEqual(HttpMethod.Head, req.Method);
            if (count > 1) Assert.AreEqual(Tag, req.Headers.IfMatch.Single().Tag);
            var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent([])};
            response.Headers.ETag = EntityTagHeaderValue.Parse(Tag);
            return response;
        });
        var downloader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var payload = await downloader.DownloadAsync(empty, DirectoryPath, ClientName, Resolve, null, default);
        Assert.AreEqual(0, new FileInfo(payload).Length);
        Assert.AreEqual(new VerifiedOriginalPayload(payload, 0, Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()))),
            await downloader.VerifyCompletedAsync(empty, DirectoryPath, default));
        Assert.AreEqual(2, count);
        File.Move(payload, Path.Combine(_root, "empty-final"));
        Assert.IsTrue((await downloader.InspectHandedOffAsync(empty, DirectoryPath, default)).IsComplete);
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.VerifyCompletedAsync(empty, DirectoryPath, default));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => downloader.InspectAsync(empty, DirectoryPath, default));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () =>
            downloader.DownloadAsync(empty, DirectoryPath, ClientName, Resolve, null, default));
        Assert.IsFalse(File.Exists(payload));
    }

    [TestMethod]
    public async Task Published_original_returns_segment_verified_full_hash_offline_and_can_be_verified_again()
    {
        using var services = Services(req => Reply(req));
        var path = await Download(services.GetRequiredService<IAuthorizedOriginalHttpDownloader>());
        using var offline = Services(_ => throw new AssertFailedException("Local handoff opened HTTP"));
        var reader = offline.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var expected = new VerifiedOriginalPayload(path, _identity.Length, Convert.ToHexStringLower(SHA256.HashData(_original)));
        var journal = Path.Combine(DirectoryPath, "checkpoint");
        var previousJournal = await File.ReadAllBytesAsync(journal);
        int reads = 0;
        Assert.AreEqual(expected, await reader.VerifyCompletedAsync(_identity, DirectoryPath, default, () =>
        {
            reads++;
            return ValueTask.CompletedTask;
        }));
        Assert.IsTrue(reads >= 2);
        Assert.AreEqual(expected, await reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        CollectionAssert.AreEqual(previousJournal, await File.ReadAllBytesAsync(journal));
    }

    [TestMethod]
    [DataRow("wrong owner")]
    [DataRow("wrong attempt")]
    [DataRow("before rename")]
    [DataRow("unsealed")]
    [DataRow("same size changed first")]
    [DataRow("same size changed last")]
    [DataRow("bad segment hash")]
    [DataRow("short")]
    [DataRow("long")]
    [DataRow("missing")]
    [DataRow("unregistered entry")]
    public async Task Completed_only_handoff_rejects_invalid_or_unpublished_data_without_repair(string mode)
    {
        using var services = Services(req => Reply(req));
        var payload = await Download(services.GetRequiredService<IAuthorizedOriginalHttpDownloader>());
        var identity = _identity;
        var journal = Path.Combine(DirectoryPath, "checkpoint");
        switch (mode)
        {
            case "wrong owner": identity = identity with { AccountVersion = "other-owner" }; break;
            case "wrong attempt": identity = identity with { AttemptScope = "another-attempt" }; break;
            case "before rename": File.Move(payload, payload + ".download"); break;
            case "unsealed":
                using (var write = File.OpenWrite(journal)) write.SetLength(write.Length - 1);
                break;
            case "same size changed first": case "same size changed last":
                var position = mode.EndsWith("first") ? 0 : _original.Length - 1;
                using (var write = File.OpenWrite(payload))
                {
                    write.Position = position;
                    write.WriteByte((byte)(_original[position] ^ 255));
                }
                break;
            case "bad segment hash":
                using (var write = File.Open(journal, FileMode.Open, FileAccess.ReadWrite))
                {
                    write.Position = write.Length - 2;
                    var previous = write.ReadByte();
                    write.Position--;
                    write.WriteByte((byte)(previous ^ 255));
                }
                break;
            case "long": case "short":
                using (var write = File.OpenWrite(payload)) write.SetLength(write.Length + (mode == "long" ? 1 : -1));
                break;
            case "missing": File.Delete(payload); break;
            case "unregistered entry": await File.WriteAllBytesAsync(payload + ".unknown", []); break;
        }
        var entries = Directory.GetFileSystemEntries(DirectoryPath);
        var previousJournal = await File.ReadAllBytesAsync(journal);
        using var offline = Services(_ => throw new AssertFailedException("Unexpected HTTP request."));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () =>
            offline.GetRequiredService<IAuthorizedOriginalHttpDownloader>().VerifyCompletedAsync(identity, DirectoryPath, default));
        CollectionAssert.AreEquivalent(entries, Directory.GetFileSystemEntries(DirectoryPath));
        CollectionAssert.AreEqual(previousJournal, await File.ReadAllBytesAsync(journal));
    }

    [TestMethod]
    public async Task Missing_or_partial_directory_cannot_create_a_handoff_or_extend_the_checkpoint()
    {
        using var offline = Services(_ => throw new AssertFailedException("Unexpected HTTP request."));
        var reader = offline.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        Assert.IsFalse(Directory.Exists(DirectoryPath));
        await InterruptAfterOneSegment();
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        Assert.AreEqual(new AuthorizedOriginalCheckpoint(true, Segment, false), await reader.InspectAsync(_identity, DirectoryPath, default));
    }

    [TestMethod]
    public async Task Substituted_link_and_special_file_are_rejected_before_reading_data()
    {
        if (OperatingSystem.IsWindows()) return;
        using var services = Services(req => Reply(req));
        var reader = services.GetRequiredService<IAuthorizedOriginalHttpDownloader>();
        var payload = await Download(reader);
        var outside = Path.Combine(_root, "another-owner");
        File.Move(payload, outside);
        File.CreateSymbolicLink(payload, outside);
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        File.Delete(payload);
        Assert.AreEqual(0, HardLink(outside, payload));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        File.Delete(payload);
        Assert.AreEqual(0, CreatePipe(payload, 0x180));
        await Expect("ORIGINAL_HTTP_CHECKPOINT_INVALID", () => reader.VerifyCompletedAsync(_identity, DirectoryPath, default));
        CollectionAssert.AreEqual(_original, await File.ReadAllBytesAsync(outside));
    }

    [TestMethod]
    public async Task Signed_request_is_not_exposed_to_HTTP_factory_loggers_even_at_trace_level()
    {
        var logs = new CapturedLogs();
        using var services = Services(req => Reply(req), logs);
        services.GetRequiredService<ILoggerFactory>().CreateLogger("control").LogTrace("capture-enabled");
        await Download(services.GetRequiredService<IAuthorizedOriginalHttpDownloader>());
        Assert.IsTrue(logs.Messages.Contains("capture-enabled"));
        foreach (var message in logs.Messages)
        {
            Assert.IsFalse(message.Contains(Secret));
            Assert.IsFalse(message.Contains("cdn.example.org"));
            Assert.IsFalse(message.Contains(Tag));
        }
    }

    private sealed class CapturedLogs : ILoggerProvider, ILogger
    {
        internal readonly ConcurrentQueue<string> Messages = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Dispose() { }
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> format) => Messages.Enqueue(format(state, exception) + exception);
    }

    private static async Task Expect(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsExactlyAsync<AuthorizedOriginalDownloadException>(action);
        Assert.AreEqual(code, error.Code);
    }
}
