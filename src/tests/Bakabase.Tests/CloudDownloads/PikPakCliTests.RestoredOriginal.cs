using System.Text.Json;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bakabase.Service.Components.CloudDownloads.WebProviders;

namespace Bakabase.Tests.CloudDownloads;

public sealed partial class PikPakCliTests
{
    private RestoredOriginal _restored = null!;

    [DataTestMethod]
    [DataRow("2.39 GB", "https://cdn.mypikpak.net/data?signature=fixture", null)]
    [DataRow("2.40 GB", "https://cdn.mypikpak.net/data?signature=fixture", "PIKPAK_REMOTE_CHANGED")]
    [DataRow("2.39 GB", "http://cdn.mypikpak.net/data", "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")]
    [DataRow("2.39 GB", "https://mypikpak.net.unrelated.test/data", "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")]
    [DataRow("2.39 GB", "https://secret@cdn.mypikpak.net/data", "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")]
    [DataRow("2.39 GB", null, "PIKPAK_ORIGINAL_FILE_UNAVAILABLE")]
    public void OfficialOriginalUsesRoundedDisplaySizeAndOnlyTheAllowlistedDownloadField(string size, string? downloadUrl,
        string? expectedProblem)
    {
        var file = new PikPakCliCloudProvider.RemoteFile("file", "inside/example.zip", 2_565_000_000,
            "manifest-hash", "version");
        var response = JsonSerializer.SerializeToElement(new {id = file.Id, type = "file", name = "example.zip",
            size, hash = file.Hash, download_url = downloadUrl, play_url = "https://cdn.mypikpak.net/video"});
        if (expectedProblem == null)
        {
            Assert.AreEqual(downloadUrl, PikPakCliCloudProvider.ReadRestoredOriginalUrl(response, file, "example.zip").AbsoluteUri);
            return;
        }
        try
        {
            PikPakCliCloudProvider.ReadRestoredOriginalUrl(response, file, "example.zip");
            Assert.Fail("The rejected response returned a URL.");
        }
        catch (PikPakCliException problem)
        { Assert.AreEqual(expectedProblem, problem.Code); }
    }

    [TestMethod]
    public async Task UnboundOrModifiedHttpStagingCannotAuthorizeAnotherPayloadAttempt()
    {
        await Login();
        _native.FailPayload = true;
        _native.RemoveFailedNativePartial = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        var oldPayload = _native.Calls.Last(call => call[0] == "download")[3];
        var unknown = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(oldPayload)!, "restored-http-v1", "unclaimed"));
        await File.WriteAllTextAsync(Path.Combine(unknown.FullName, "opaque"), "keep");
        Assert.IsFalse((await _provider.CheckPreparedDownloadRetryAsync(Request, default)).Available);
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(unknown.FullName, "opaque")));
        Assert.AreEqual(0, _restored.Transfers.Count);
        unknown.Delete(recursive: true);

        _restored.Fail = true;
        var interrupted = await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _provider.RetryPreparedDownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_NETWORK_UNAVAILABLE", interrupted.Message);
        var preserved = _restored.Transfers.Single().Directory;
        Assert.IsTrue((await _provider.CheckPreparedDownloadRetryAsync(Request, default)).Available);
        await File.WriteAllTextAsync(Path.Combine(preserved, "unrecognized-file"), "preserve");
        Assert.IsFalse((await _provider.CheckPreparedDownloadRetryAsync(Request, default)).Available);
        Assert.AreEqual("preserve", await File.ReadAllTextAsync(Path.Combine(preserved, "unrecognized-file")));
        var journal = Directory.GetFiles(Path.Combine(_root, "appdata"), "download.json", SearchOption.AllDirectories).Single();
        Assert.IsFalse((await File.ReadAllTextAsync(journal)).Contains("signature", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task OriginalLocationRejectionRetainsItsProviderReasonWithoutLeakingALink()
    {
        await Login();
        _native.FailPayload = true;
        _native.RemoveFailedNativePartial = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        _native.OriginalUrl = "https://untrusted.invalid/file?signature=keep-private";
        var failure = await Assert.ThrowsExceptionAsync<CloudProviderException>(() =>
            _provider.RetryPreparedDownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_ORIGINAL_FILE_UNAVAILABLE", failure.Message);
        Assert.IsNull(failure.InnerException);
        Assert.AreEqual(0, _restored.Transfers.Count);
        Assert.AreEqual(1, _native.Downloads);
    }

    private sealed class RestoredOriginal : IAuthorizedOriginalHttpDownloader
    {
        internal bool Fail;
        internal List<(AuthorizedOriginalIdentity Identity, string Directory, Uri Original)> Transfers { get; } = [];

        public Task<AuthorizedOriginalCheckpoint> InspectAsync(AuthorizedOriginalIdentity identity,
            string checkpointDirectory, CancellationToken ct)
        {
            if (!Directory.Exists(checkpointDirectory))
                return Task.FromResult(new AuthorizedOriginalCheckpoint(false, 0, false));
            var files = Directory.GetFileSystemEntries(checkpointDirectory).Select(Path.GetFileName).ToArray();
            if (files.Any(name => name is not ("checkpoint-part" or "payload")) ||
                !Transfers.Any(call => call.Directory == checkpointDirectory && call.Identity == identity))
                throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_CHECKPOINT_INVALID");
            return Task.FromResult(new AuthorizedOriginalCheckpoint(files.Length != 0,
                files.Contains("payload") ? identity.Length : 1, files.Contains("payload")));
        }

        public async Task<AuthorizedOriginalCheckpoint> InspectHandedOffAsync(AuthorizedOriginalIdentity identity,
            string checkpointDirectory, CancellationToken ct)
        {
            var files = Directory.GetFileSystemEntries(checkpointDirectory);
            if (files.Length != 1 || Path.GetFileName(files[0]) != "checkpoint" ||
                await File.ReadAllTextAsync(files[0], ct) != identity.AttemptScope)
                throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_CHECKPOINT_INVALID");
            return new(true, identity.Length, true);
        }

        public async Task<string> DownloadAsync(AuthorizedOriginalIdentity identity, string checkpointDirectory,
            string httpClientName, Func<CancellationToken, Task<Uri>> resolveOriginal,
            Func<HttpTransferProgress, Task>? progress, CancellationToken ct)
        {
            Assert.AreEqual(PublicShareDownloadClients.PikPakOfficialOriginal, httpClientName);
            var uri = await resolveOriginal(ct);
            Transfers.Add((identity, checkpointDirectory, uri));
            Directory.CreateDirectory(checkpointDirectory);
            var partial = Path.Combine(checkpointDirectory, "checkpoint-part");
            if (Fail)
            {
                await File.WriteAllTextAsync(partial, "a", ct);
                throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_TRANSFER_FAILED");
            }
            Assert.AreEqual("a", await File.ReadAllTextAsync(partial, ct));
            await resolveOriginal(ct);
            var payload = Path.Combine(checkpointDirectory, "payload");
            await File.WriteAllTextAsync(payload, "abc", ct);
            await File.WriteAllTextAsync(Path.Combine(checkpointDirectory, "checkpoint"), identity.AttemptScope, ct);
            File.Delete(partial);
            return payload;
        }
    }
}
