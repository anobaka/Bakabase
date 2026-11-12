using System.Net;
using System.Security.Cryptography;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class ResolvedShareProviderTests
{
    private string _root = null!;
    [TestInitialize] public void Initialize() => _root = Directory.CreateTempSubdirectory("resolved-share-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task Readonly_recovery_checks_every_original_without_creating_state_or_payload_files()
    {
        var downloader = new Transfer(); var provider = Provider(downloader);
        provider.Files = [new("one", "first.bin", 8, "v1"), new("two", "second.bin", 8, "v1")];
        var result = await provider.RevalidateDownloadAsync(Request(), default);
        Assert.IsTrue(result.Available, result.Problem);
        Assert.AreEqual(2, provider.Refreshes.Count);
        Assert.AreEqual(0, downloader.Calls.Count);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(_root).Any());
    }

    [TestMethod]
    public async Task Readonly_recovery_rejects_a_visible_file_without_an_original_download_URL()
    {
        var downloader = new Transfer(); var provider = Provider(downloader); provider.OriginalUnavailable = true;
        var result = await provider.RevalidateDownloadAsync(Request(), default);
        Assert.IsFalse(result.Available);
        StringAssert.Contains(result.Problem!, "PIKPAK_ORIGINAL_FILE_UNAVAILABLE");
        Assert.AreEqual(0, downloader.Calls.Count);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(_root).Any());
    }

    [TestMethod]
    public async Task Readonly_recovery_preserves_the_existing_manifest_and_rejects_remote_changes()
    {
        var downloader = new Transfer(); var provider = Provider(downloader); provider.OriginalUnavailable = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.DownloadAsync(Request(), _ => Task.CompletedTask, default));
        var manifest = Directory.GetFiles(Path.Combine(_root, "app"), "manifest.json", SearchOption.AllDirectories).Single();
        var before = File.ReadAllText(manifest);
        provider.OriginalUnavailable = false;
        Assert.IsTrue((await provider.RevalidateDownloadAsync(Request(), default)).Available);
        provider.Files = [new("one", "folder/original.bin", 9, "changed")];
        var changed = await provider.RevalidateDownloadAsync(Request(), default);
        Assert.IsFalse(changed.Available);
        StringAssert.Contains(changed.Problem!, "manifest or destination changed");
        Assert.AreEqual(before, File.ReadAllText(manifest));
        Assert.AreEqual(0, downloader.Calls.Count);
    }

    [TestMethod]
    public async Task Native_engine_is_used_and_verified_receipts_replay_without_a_second_transfer()
    {
        var downloader = new Transfer();
        var provider = Provider(downloader);
        var request = Request();
        var first = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        var repeated = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, downloader.Calls.Count);
        Assert.AreEqual(PublicShareDownloadClients.PikPak, downloader.Calls[0].HttpClientName);
        Assert.IsFalse(downloader.Calls[0].AllowOverwrite);
        CollectionAssert.AreEqual(first.Files.ToArray(), repeated.Files.ToArray());
        Assert.AreEqual("original", File.ReadAllText(first.Files[0]));
        var receipts = Directory.GetFiles(Path.Combine(_root, "app"), "completed.json", SearchOption.AllDirectories);
        Assert.AreEqual(1, receipts.Length);
        StringAssert.Contains(File.ReadAllText(receipts[0]), Convert.ToHexString(SHA256.HashData("original"u8)).ToLowerInvariant());
        Assert.IsFalse(Directory.GetFiles(Path.Combine(_root, "app"), "*.json", SearchOption.AllDirectories)
            .Any(path => File.ReadAllText(path).Contains("temporary-secret")));
    }

    [TestMethod]
    public async Task Replayed_operation_rejects_changed_local_bytes_and_preserves_them()
    {
        var downloader = new Transfer(); var provider = Provider(downloader); var request = Request();
        var result = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        await File.WriteAllTextAsync(result.Files[0], "useredit");
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("useredit", File.ReadAllText(result.Files[0]));
        Assert.AreEqual(1, downloader.Calls.Count);
    }

    [TestMethod]
    public async Task Changed_remote_manifest_requires_a_new_operation_instead_of_silent_partial_success()
    {
        var provider = Provider(new Transfer()); var request = Request();
        await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        provider.Files = [new("one", "folder/original.bin", 9, "changed")];
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(request.OutputDirectory, "folder", "original.bin")));
    }

    [TestMethod]
    public async Task Expired_original_url_is_refreshed_once_and_never_persisted_in_the_manifest()
    {
        var downloader = new Transfer {FailFirst = true};
        var provider = Provider(downloader);
        await provider.DownloadAsync(Request(), _ => Task.CompletedTask, default);
        CollectionAssert.AreEqual(new[] {false, true}, provider.Refreshes.ToArray());
        Assert.AreEqual(2, downloader.Calls.Count);
        Assert.AreNotEqual(downloader.Calls[0].Url, downloader.Calls[1].Url);
    }

    [TestMethod]
    public async Task Unrelated_existing_destination_is_never_overwritten()
    {
        var downloader = new Transfer(); var provider = Provider(downloader); var request = Request();
        Directory.CreateDirectory(Path.Combine(request.OutputDirectory, "folder"));
        var existing = Path.Combine(request.OutputDirectory, "folder", "original.bin");
        File.WriteAllText(existing, "user-content");
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
        Assert.AreEqual("user-content", File.ReadAllText(existing));
        Assert.AreEqual(0, downloader.Calls.Count);
    }

    [TestMethod]
    public async Task Truncated_or_wrong_checksum_body_cannot_be_published_or_receipted()
    {
        foreach (var wrongSize in new[] {true, false})
        {
            var provider = Provider(new Transfer {Body = wrongSize ? "short" : "badbytes"});
            if (!wrongSize) provider.Files = [new("one", "folder/original.bin", 8, "v1", new string('0', 64))];
            var request = Request() with {OperationId = Guid.NewGuid().ToString("N")};
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => provider.DownloadAsync(request, _ => Task.CompletedTask, default));
            Assert.IsFalse(File.Exists(Path.Combine(request.OutputDirectory, "folder", "original.bin")));
        }
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(_root, "app"), "completed.json", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task Cancellation_is_propagated_and_a_later_retry_can_complete()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new Transfer {Wait = started}; var provider = Provider(downloader); var request = Request();
        using var cts = new CancellationTokenSource();
        var running = provider.DownloadAsync(request, _ => Task.CompletedTask, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cts.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await running);
        Assert.IsFalse(File.Exists(Path.Combine(request.OutputDirectory, "folder", "original.bin")));
        downloader.Wait = null;
        var result = await provider.DownloadAsync(request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, result.Files.Count);
    }

    [DataTestMethod]
    [DataRow("../escape")]
    [DataRow("/absolute")]
    [DataRow("folder/CON.txt")]
    [DataRow("folder/a\\b")]
    [DataRow("folder/name.")]
    public void Unsafe_paths_are_rejected_before_a_transfer(string path) =>
        Assert.ThrowsException<InvalidDataException>(() => ResolvedShareCloudProvider.ValidateManifest([new("one", path, 1)]));

    [TestMethod]
    public void Duplicate_case_conflicts_file_directory_conflicts_and_empty_lists_are_explicit_failures()
    {
        foreach (var files in new PublicShareFile[][]
        {
            [], [new("one", "Name.bin", 1), new("two", "name.bin", 1)],
            [new("one", "folder", 1), new("two", "folder/inside", 1)],
            [new("one", "a.bin", 1), new("one", "b.bin", 1)]
        }) Assert.ThrowsException<InvalidDataException>(() => ResolvedShareCloudProvider.ValidateManifest(files));
    }

    [TestMethod]
    public async Task Symlink_output_is_rejected_before_discovery_or_transfer()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Symlink creation requires Windows developer mode."); return; }
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside"));
        var output = Path.Combine(_root, "output"); Directory.CreateSymbolicLink(output, outside.FullName);
        var downloader = new Transfer(); var provider = Provider(downloader);
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => provider.DownloadAsync(Request(), _ => Task.CompletedTask, default));
        Assert.AreEqual(0, provider.Discoveries); Assert.AreEqual(0, downloader.Calls.Count);
        Directory.Delete(output);
    }

    private FixtureProvider Provider(Transfer downloader) => new(Path.Combine(_root, "app"), downloader);
    private CloudDownloadRequest Request() => new("same-operation", new CloudSource
    {Id = "fixture", ProviderId = "pikpak", Url = "https://mypikpak.com/s/fixture", AccessCode = "private-access"}, null, null, Path.Combine(_root, "output"));
    private sealed class FixtureProvider(string appData, IHttpDownloader downloader)
        : ResolvedShareCloudProvider(() => appData, downloader, new PublicShareResolverTests.Clients(new PublicShareResolverTests.Reply(_ => throw new AssertFailedException("No network expected"))))
    {
        public override string ProviderId => "pikpak";
        protected override string ClientName => PublicShareDownloadClients.PikPak;
        public IReadOnlyList<PublicShareFile> Files { get; set; } = [new("one", "folder/original.bin", 8, "v1")];
        public List<bool> Refreshes { get; } = [];
        public int Discoveries;
        public bool OriginalUnavailable;
        internal override void ValidateSource(CloudSource source) { }
        internal override Task<PublicShareResolution> ResolveAsync(CloudSource source, HttpClient client, CancellationToken ct)
        {
            Discoveries++;
            return Task.FromResult(new PublicShareResolution(Files, (_, refresh, token) =>
            {
                token.ThrowIfCancellationRequested(); Refreshes.Add(refresh);
                if (OriginalUnavailable) throw new CloudProviderException("PIKPAK_ORIGINAL_FILE_UNAVAILABLE: no original URL");
                return Task.FromResult(new Uri("https://dl.mypikpak.com/original?temporary-secret=" + (refresh ? "new" : "old")));
            }));
        }
    }
    private sealed class Transfer : IHttpDownloader
    {
        public List<HttpDownloadRequest> Calls { get; } = [];
        public bool FailFirst;
        public string Body = "original";
        public TaskCompletionSource? Wait;
        public async Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            if (FailFirst && Calls.Count == 1) throw new HttpRequestException("expired", null, HttpStatusCode.Forbidden);
            if (Wait != null) { Wait.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
            Directory.CreateDirectory(request.Directory);
            var path = Path.Combine(request.Directory, request.FileName!);
            await File.WriteAllTextAsync(path, Body, cancellationToken);
            if (progress != null) await progress(100, "completed");
            return path;
        }
    }
}
