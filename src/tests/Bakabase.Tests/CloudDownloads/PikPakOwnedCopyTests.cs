using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakOwnedCopyTests
{
    private string _root = null!;
    private Native _native = null!;
    private PikPakCliAccountService _accounts = null!;
    private PikPakCliCloudProvider _provider = null!;
    private CloudDownloadRequest _request = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pikpak-owned-copy-" + Guid.NewGuid().ToString("N"));
        _native = new();
        _accounts = new(Path.Combine(_root, "appdata"), _native);
        _provider = new(_accounts, null, _ => Task.CompletedTask);
        _request = new("operation", new() {Id = "source", ProviderId = "pikpak", MethodId = "pikpak.share",
            Kind = "share", Url = "https://mypikpak.com/s/fixture"}, null, null, Path.Combine(_root, "output"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task LocalCompletionOnlyTrashesProvenFileIdsAndRepeatingAfterRestartDoesNotSendAnotherTrash()
    {
        var (copy, proof) = await Download();
        Assert.AreEqual(copy, await _provider.PrepareOwnedCopyAsync(_request, default));
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual("trashed", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);

        _provider = new(new PikPakCliAccountService(Path.Combine(_root, "appdata"), _native));
        Assert.AreEqual("trashed", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);
        CollectionAssert.AreEqual(new[] {"own-file"}, _native.TrashRequests.Single());
        Assert.IsTrue(_native.CreatedFolder);
        Assert.IsTrue(File.Exists(proof.Files.Single()));
    }

    [TestMethod]
    public async Task DefaultRetentionAndPreviouslyUnenrolledTransfersCannotBeSelectedForTrash()
    {
        await Authorize(enabled: false);
        var calls = _native.Calls;
        Assert.IsFalse(await _provider.IsOwnedCopyEnabledAsync(_request, default));
        Assert.AreEqual(calls, _native.Calls);
        Assert.IsNull(await _provider.PrepareOwnedCopyAsync(_request, default));
        Assert.AreEqual(0, _native.Restores);
        await _provider.DownloadAsync(_request, _ => Task.CompletedTask, default);
        await SetCleanup(true);
        calls = _native.Calls;
        Assert.IsTrue(await _provider.IsOwnedCopyEnabledAsync(_request, default));
        Assert.AreEqual(calls, _native.Calls);

        Assert.IsNull(await _provider.PrepareOwnedCopyAsync(_request, default));
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OlderPrewritePlanCanEnrollBeforeItsFirstMkdirButCannotAdoptASameNameFolder(bool existingFolder)
    {
        await Authorize(enabled: false);
        _native.AccountOverQuota = true;
        var blocked = await Assert.ThrowsExceptionAsync<CloudProviderException>(
            () => _provider.DownloadAsync(_request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", blocked.Message);
        Assert.IsFalse(_native.CreatedFolder);
        Assert.AreEqual(0, _native.Restores);

        await SetCleanup(true);
        _native.AccountOverQuota = false;
        if (existingFolder)
        {
            var operations = Path.Combine(_root, "appdata", "operations");
            var journal = Directory.EnumerateFiles(operations, "transfer.json", SearchOption.AllDirectories).Single();
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(journal));
            _native.AddExistingFolder(saved.RootElement.GetProperty("folderName").GetString()!);
            var collision = await Assert.ThrowsExceptionAsync<CloudProviderException>(
                () => _provider.PrepareOwnedCopyAsync(_request, default));
            Assert.AreEqual("PIKPAK_RESTORE_REVIEW_REQUIRED", collision.Message);
            Assert.AreEqual(0, _native.Restores);
            Assert.AreEqual(0, _native.MkdirCount);
            return;
        }

        var copy = await _provider.PrepareOwnedCopyAsync(_request, default);
        Assert.IsNotNull(copy);
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual(1, _native.MkdirCount);
    }

    [DataTestMethod]
    [DataRow("extra-empty-folder")]
    [DataRow("foreign-origin")]
    [DataRow("lost-mkdir-response")]
    public async Task UnprovenInitialOwnershipNeverEnablesRemoteTrashButFilesCanStillDownload(string mode)
    {
        await Authorize(enabled: true);
        _native.RemoteChange = mode;
        if (mode == "lost-mkdir-response")
        {
            await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.PrepareOwnedCopyAsync(_request, default));
            Assert.IsTrue(_native.CreatedFolder);
            Assert.AreEqual(0, _native.Restores);
        }

        Assert.IsNull(await _provider.PrepareOwnedCopyAsync(_request, default));
        var result = await _provider.DownloadAsync(_request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, result.Files.Count);
        Assert.AreEqual(1, _native.Restores);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [DataTestMethod]
    [DataRow("extra-file")]
    [DataRow("extra-empty-folder")]
    [DataRow("same-size-remote-change")]
    [DataRow("renamed-root")]
    [DataRow("moved-root")]
    [DataRow("moved-file")]
    public async Task ChangedTreeRequiresReviewBeforeAnyTrash(string mode)
    {
        var (copy, proof) = await Download();
        _native.RemoteChange = mode;
        var result = await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default);
        Assert.AreEqual("reviewRequired", result.Status);
        Assert.AreEqual("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED", result.ReasonCode);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [TestMethod]
    public async Task ChangedLocalContentsOrChangedAccountNeverReleaseTheCloudCopy()
    {
        var (copy, proof) = await Download();
        var path = proof.Files.Single();
        await File.WriteAllTextAsync(path, "xyz");
        var edited = proof with {VerifiedFiles = [new(path, 3, await PikPakCliStorage.FileHash(path, default))]};
        var rejected = await _provider.ReleaseOwnedCopyAsync(_request, copy, edited, default);
        Assert.AreEqual("PIKPAK_OWNED_COPY_LOCAL_FILES_CHANGED", rejected.ReasonCode);

        await File.WriteAllTextAsync(path, "abc");
        var previous = await _accounts.GetAsync(default);
        var session = await _accounts.StartAsync(default);
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.AreNotEqual(previous.AccountVersion, (await _accounts.GetAsync(default)).AccountVersion);
        rejected = await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default);
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", rejected.ReasonCode);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [TestMethod]
    public async Task SameSizeLocalReplacementDuringRemoteValidationCannotBeTrashed()
    {
        var (copy, proof) = await Download();
        var path = proof.Files.Single();
        _native.WhileListingCloud = () =>
        {
            var modified = File.GetLastWriteTimeUtc(path);
            var replacement = path + ".replacement";
            File.WriteAllText(replacement, "xyz");
            File.SetLastWriteTimeUtc(replacement, modified);
            File.Move(replacement, path, overwrite: true);
        };

        var result = await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default);
        Assert.AreEqual("reviewRequired", result.Status);
        Assert.AreEqual("PIKPAK_OWNED_COPY_LOCAL_FILES_CHANGED", result.ReasonCode);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [TestMethod]
    public async Task TurningOffTheSettingAfterDownloadRetainsTheCopy()
    {
        var (copy, proof) = await Download();
        await SetCleanup(false);
        Assert.AreEqual("retained", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);
        Assert.AreEqual(0, _native.TrashRequests.Count);
    }

    [DataTestMethod]
    [DataRow("awaiting-bin-receipt")]
    [DataRow("wrong-bin-parent")]
    [DataRow("write-not-confirmed")]
    public async Task UnknownTrashSubmissionNeverResendsAndCanLaterReconcileAnExactReceipt(string mode)
    {
        var (copy, proof) = await Download();
        _native.TrashState = mode;
        var unknown = await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default);
        Assert.AreEqual("retryable", unknown.Status);
        Assert.AreEqual("PIKPAK_OWNED_COPY_TRASH_UNCONFIRMED", unknown.ReasonCode);

        _provider = new(new PikPakCliAccountService(Path.Combine(_root, "appdata"), _native));
        Assert.AreEqual("retryable", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);
        Assert.AreEqual(1, _native.TrashRequests.Count);

        _native.TrashState = "complete";
        _native.FileTrashed = true;
        Assert.AreEqual("trashed", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);
        Assert.AreEqual(1, _native.TrashRequests.Count);
    }

    [TestMethod]
    public async Task LostTrashResponseWithExactBinProofCompletesTheSameAttempt()
    {
        var (copy, proof) = await Download();
        _native.TrashState = "response-lost";
        Assert.AreEqual("trashed", (await _provider.ReleaseOwnedCopyAsync(_request, copy, proof, default)).Status);
        Assert.AreEqual(1, _native.TrashRequests.Count);
    }

    private async Task Authorize(bool enabled)
    {
        var session = await _accounts.StartManageFilesAuthorizationAsync(default);
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        if (enabled) await SetCleanup(true);
    }

    private async Task SetCleanup(bool enabled)
    {
        var setting = await _accounts.GetAsync(default);
        await _accounts.SetCleanupSettingsAsync(new(setting.AccountId!, setting.AccountVersion!, enabled), default);
    }

    private async Task<(CloudOwnedCopyReceipt Copy, CloudVerifiedLocalDownload Proof)> Download()
    {
        await Authorize(enabled: true);
        var copy = await _provider.PrepareOwnedCopyAsync(_request, default);
        Assert.IsNotNull(copy);
        var result = await _provider.DownloadAsync(_request, _ => Task.CompletedTask, default);
        var path = result.Files.Single();
        var hash = await PikPakCliStorage.FileHash(path, default);
        return (copy!, new(10, 20, _request.OutputDirectory, result.Files, "verified-original")
        {
            VerifiedFiles = [new(path, 3, hash)
            {
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
                ContentGeneration = ArtifactFileIdentity.ReadContentGeneration(path)
            }]
        });
    }

    private sealed class Native : IPikPakCliProcess
    {
        public List<string[]> TrashRequests { get; } = [];
        public bool CreatedFolder, FileTrashed, AccountOverQuota;
        public int Restores, MkdirCount, Calls;
        public string RemoteChange = "";
        public string TrashState = "complete";
        public Action? WhileListingCloud;
        private string? _folderName;
        private readonly Dictionary<string, string> _scopes = new(StringComparer.Ordinal);

        public async Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
            bool payload = false, Action? observeProgress = null)
        {
            Calls++;
            var command = arguments[0];
            var profileId = Path.GetFileName(profile);
            string? Value(string option)
            {
                var position = arguments.ToList().IndexOf(option);
                return position < 0 ? null : arguments[position + 1];
            }

            return command switch
            {
                "auth" when arguments[1] == "status" =>
                    JsonSerializer.SerializeToElement(new {logged_in = true, user_id = "provider-owner", name = "Synthetic",
                        storage = new {used = AccountOverQuota ? "7 B" : "0 B", total = "6 B"}}),
                "auth" when arguments[1] == "login" && arguments.Contains("--continue") =>
                    JsonSerializer.SerializeToElement(new {status = "success", scopes = _scopes[profileId].Split(',')}),
                "auth" when arguments[1] == "login" => BeginAuth(),
                "share" when arguments[1] == "get" => ShareContents(Value("--parent")),
                "share" when arguments[1] == "restore" => Restore(),
                "mkdir" => Mkdir(arguments[1]),
                "ls" => AccountFiles(Value("--parent")),
                "rm" => Trash(arguments.Skip(1).ToArray()),
                "trash" when arguments[1] == "ls" => TrashFiles(),
                "download" => await WriteDownload(),
                _ => throw new AssertFailedException("Unexpected native command"),
            };

            JsonElement BeginAuth()
            {
                _scopes[profileId] = Value("--scope")!;
                return J("""{"device_code":"sandbox-only","user_code":"ABCD-EFGH","verification_uri":"https://mypikpak.com/drive/activate","expires_in":300,"interval":2}""");
            }

            async Task<JsonElement> WriteDownload()
            {
                Assert.AreEqual("own-file", arguments[1]);
                await File.WriteAllTextAsync(Value("--output")!, "abc", ct);
                return J("{}");
            }
        }

        public void AddExistingFolder(string name)
        {
            _folderName = name;
            CreatedFolder = true;
        }

        private JsonElement Mkdir(string name)
        {
            AddExistingFolder(name);
            MkdirCount++;
            if (RemoteChange == "lost-mkdir-response") throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
            return J("""{"id":"owned-root"}""");
        }

        private JsonElement Restore()
        {
            Restores++;
            return J("""{"restore_status":"COMPLETE","restore_task_id":"restore-receipt"}""");
        }

        private static JsonElement ShareContents(string? parent) => parent switch
        {
            null => J("""{"status":"OK","file_list":[{"id":"shared-folder","name":"nested","kind":"folder"}]}"""),
            "shared-folder" => J("""
                {"files":[{"id":"shared-file","name":"original.bin","kind":"file","size":"3 B"},
                          {"id":"shared-empty","name":"empty","kind":"folder"}]}
                """),
            "shared-empty" => J("""{"files":[]}"""),
            _ => throw new AssertFailedException("Unexpected share folder.")
        };

        private JsonElement AccountFiles(string? parent)
        {
            if (parent == null && WhileListingCloud is { } callback)
            {
                WhileListingCloud = null;
                callback();
            }
            object original = new {id = "own-file", name = "original.bin", kind = "drive#file", size = "3",
                hash = RemoteChange == "same-size-remote-change" ? "edited-hash" : "remote-hash",
                modified_time = "2026-11-24T08:00:00Z",
                @params = new {original_share_id = RemoteChange == "foreign-origin" ? "other" : "fixture"}};
            List<object> files = parent switch
            {
                null => !CreatedFolder || RemoteChange == "moved-root" ? [] :
                    [new {id = "owned-root", name = RemoteChange == "renamed-root" ? "changed" : _folderName, kind = "drive#folder"}],
                "owned-root" => Restores == 0 ? [] : [new {id = "owned-folder", name = "nested", kind = "drive#folder"}],
                "owned-folder" => [new {id = "owned-empty", name = "empty", kind = "drive#folder"}],
                "owned-empty" or "extra-empty" => [],
                _ => throw new AssertFailedException("Unexpected cloud folder."),
            };
            if (parent == "owned-folder" && !FileTrashed && RemoteChange != "moved-file") files.Add(original);
            if (parent == "owned-folder" && RemoteChange == "extra-file")
                files.Add(new {id = "user-added", name = "usermedia.bin", kind = "drive#file", size = "3"});
            if (parent == "owned-folder" && RemoteChange == "extra-empty-folder")
                files.Add(new {id = "extra-empty", name = "more", kind = "drive#folder"});

            return JsonSerializer.SerializeToElement(new {files});
        }

        private JsonElement Trash(string[] ids)
        {
            CollectionAssert.AreEqual(new[] {"own-file"}, ids);
            TrashRequests.Add(ids);
            if (TrashState == "write-not-confirmed") throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
            FileTrashed = true;
            if (TrashState == "response-lost") throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
            return J("""{"status":"accepted"}""");
        }

        private JsonElement TrashFiles() => !FileTrashed || TrashState == "awaiting-bin-receipt" ? J("""{"files":[]}""") :
            JsonSerializer.SerializeToElement(new {files = new[] {new {id = "own-file",
                kind = "file", name = "original.bin",
                parent_id = TrashState == "wrong-bin-parent" ? "other-parent" : "owned-folder"}}});

        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    }
}
