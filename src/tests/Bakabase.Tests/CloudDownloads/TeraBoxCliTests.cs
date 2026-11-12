using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.TeraBox;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class TeraBoxCliTests
{
    private string _root = null!;
    private Native _native = null!;
    private TeraBoxAccountService _accounts = null!;
    private TeraBoxCloudProvider _provider = null!;
    private CloudSource Source => new() {Id = "source", ProviderId = "terabox", MethodId = "terabox.cli", Url = "https://www.terabox.com/s/1abcdefgh"};
    private CloudDownloadRequest Request => new("operation", Source, null, null, Path.Combine(_root, "nas", "output"));
    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "terabox-cli-tests-" + Guid.NewGuid().ToString("N"));
        _native = new(); _accounts = new(Path.Combine(_root, "appdata"), _native); _provider = new(_accounts);
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private async Task<TeraBoxAccountViewModel> Login()
    {
        var session = await _accounts.StartAsync(default);
        Assert.AreEqual("authorized", (await _accounts.CompleteAsync(session.Id, "opaque.code+/=canary", default)).Status);
        return await _accounts.GetAsync(default);
    }
    private Task<CloudDownloadResult> Download() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);

    [TestMethod]
    public async Task MissingAccountDoesNotSpawnOrCreateFiles()
    {
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        Assert.AreEqual("TERABOX_ACCOUNT_REQUIRED", (await _provider.CheckReadyAsync(null, default)).Problem);
        Assert.IsFalse(Directory.Exists(_root)); Assert.AreEqual(0, _native.Calls.Count);
    }

    [TestMethod]
    public async Task ReadinessChecksProtectedQuotaInsteadOfTrustingLocalWhoami()
    {
        await Login(); _native.OnlineProblem = "TERABOX_ACCOUNT_REQUIRED";
        Assert.IsTrue((await _accounts.GetAsync(default)).IsConfigured);
        _native.Calls.Clear();
        Assert.AreEqual("TERABOX_ACCOUNT_REQUIRED", (await _provider.CheckReadyAsync(null, default)).Problem);
        CollectionAssert.AreEqual(new[] {"quota"}, _native.Calls.Single());
        Assert.AreEqual(0, _native.Downloads);
        Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
    }

    [TestMethod]
    public async Task UnknownQuotaResponseDoesNotPassReadinessButZeroFreeSpaceDoes()
    {
        await Login(); _native.QuotaResponse = "{\"logged_in\":true}";
        Assert.AreEqual("TERABOX_CLI_RESPONSE_INVALID", (await _provider.CheckReadyAsync(null, default)).Problem);
        _native.QuotaResponse = "{\"total\":10,\"used\":20,\"free\":0,\"usage_percent\":200}";
        Assert.IsTrue((await _provider.CheckReadyAsync(null, default)).Available);
        Assert.AreEqual(0, _native.Downloads);
    }

    [TestMethod]
    public async Task AuthorizationUsesExplicitExternalPageAndOnlyStdinForOpaqueCode()
    {
        var session = await _accounts.StartAsync(default);
        Assert.AreEqual(0, _native.Calls.Count);
        Assert.AreEqual(TeraBoxAccountService.AuthorizationUri, session.AuthorizationUri);
        _accounts = new(Path.Combine(_root, "appdata"), _native);
        Assert.AreEqual(session.Id, (await _accounts.GetActiveAuthorizationAsync(default))!.Id);
        var result = await _accounts.CompleteAsync(session.Id, "opaque.code+/=canary", default);
        Assert.AreEqual("authorized", result.Status);
        Assert.IsNull(result.AuthorizationUri);
        CollectionAssert.AreEqual(new[] {"login", "--code-stdin"}, _native.Calls[0]);
        Assert.AreEqual("opaque.code+/=canary", _native.LoginInput);
        foreach (var file in Directory.EnumerateFiles(_accounts.Root, "*.json", SearchOption.AllDirectories))
            Assert.IsFalse((await File.ReadAllTextAsync(file)).Contains("opaque.code+/=canary"));
        Assert.AreEqual(1, _native.Logins);
    }

    [TestMethod]
    public async Task LostLoginResponsePromotesCommittedConfigWithoutReplayingCode()
    {
        _native.FailAfterLogin = true;
        var session = await _accounts.StartAsync(default);
        var result = await _accounts.CompleteAsync(session.Id, "one-use", default);
        Assert.AreEqual("pending", result.Status);
        Assert.AreEqual("TERABOX_NETWORK_UNAVAILABLE", result.Problem);
        _accounts = new(Path.Combine(_root, "appdata"), _native);
        Assert.AreEqual("authorized", (await _accounts.GetActiveAuthorizationAsync(default))!.Status);
        Assert.IsTrue((await _accounts.GetAsync(default)).IsConfigured);
        Assert.AreEqual("authorized", (await _accounts.CompleteAsync(session.Id, "do-not-replay", default)).Status);
        Assert.AreEqual(1, _native.Logins);
    }

    [TestMethod]
    public async Task UnknownCodeExchangeWithoutConfigCannotReplayOrAdoptAnotherProfile()
    {
        await Login(); var old = await _accounts.GetAsync(default);
        _native.FailBeforeLogin = true;
        var session = await _accounts.StartAsync(default);
        Assert.AreEqual("pending", (await _accounts.CompleteAsync(session.Id, "different-code", default)).Status);
        Assert.AreEqual("pending", (await _accounts.GetActiveAuthorizationAsync(default))!.Status);
        Assert.AreEqual(old.AccountId, (await _accounts.GetAsync(default)).AccountId);
        var error = await Assert.ThrowsExactlyAsync<TeraBoxException>(() => _accounts.CompleteAsync(session.Id, "different-code", default));
        Assert.AreEqual("TERABOX_AUTHORIZATION_REVIEW_REQUIRED", error.Code);
        Assert.AreEqual(2, _native.Logins);
    }

    [TestMethod]
    public async Task ExpiredCancelledAndSupersededChallengesNeverPromote()
    {
        var first = await _accounts.StartAsync(default); var second = await _accounts.StartAsync(default);
        Assert.AreEqual("TERABOX_AUTHORIZATION_CHANGED", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() =>
            _accounts.CompleteAsync(first.Id, "code", default))).Code);
        await _accounts.CancelAsync(second.Id, default);
        Assert.AreEqual("cancelled", (await _accounts.GetActiveAuthorizationAsync(default))!.Status);
        Assert.AreEqual("TERABOX_AUTHORIZATION_CHANGED", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() =>
            _accounts.CompleteAsync(second.Id, "code", default))).Code);
        Assert.AreEqual(0, _native.Logins);
    }

    [TestMethod]
    public async Task MissingConfigNeverReportsGhostConnectedAndLogoutCancelsPending()
    {
        var account = await Login(); File.Delete(_accounts.Config(account.AccountId!));
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        await _accounts.StartAsync(default);
        await _accounts.LogoutAsync(default);
        Assert.AreEqual("cancelled", (await _accounts.GetActiveAuthorizationAsync(default))!.Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task MissingDependencyDoesNotConsumeCodeButCancelledExchangeCannotReplay()
    {
        var session = await _accounts.StartAsync(default); _native.NotReady = true;
        Assert.AreEqual("TERABOX_DEPENDENCY_REQUIRED", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() =>
            _accounts.CompleteAsync(session.Id, "code", default))).Code);
        Assert.AreEqual(0, _native.Logins);
        _native.NotReady = false; _native.CancelLogin = true;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => _accounts.CompleteAsync(session.Id, "code", default));
        Assert.AreEqual("TERABOX_AUTHORIZATION_REVIEW_REQUIRED", (await _accounts.GetActiveAuthorizationAsync(default))!.Problem);
        Assert.AreEqual("TERABOX_AUTHORIZATION_REVIEW_REQUIRED", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() =>
            _accounts.CompleteAsync(session.Id, "code", default))).Code);
        Assert.AreEqual(1, _native.Logins);
    }

    [TestMethod]
    public async Task InUseAccountKeepsViewsResponsiveAndBlocksReplacementOrLogout()
    {
        var account = await Login(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
        var lease = _accounts.WithAccountAsync(account.AccountId, account.AccountVersion, async _ =>
        { entered.SetResult(); await release.Task; return true; }, default);
        await entered.Task;
        try
        {
            Assert.IsTrue((await _accounts.GetAsync(default).WaitAsync(TimeSpan.FromSeconds(2))).IsConfigured);
            Assert.AreEqual("TERABOX_ACCOUNT_IN_USE", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() => _accounts.StartAsync(default))).Code);
            Assert.AreEqual("TERABOX_ACCOUNT_IN_USE", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() => _accounts.LogoutAsync(default))).Code);
        }
        finally { release.SetResult(); await lease; }
        Assert.AreEqual("TERABOX_ACCOUNT_CHANGED", (await Assert.ThrowsExactlyAsync<TeraBoxException>(() =>
            _accounts.ResolveAccountAsync(account.AccountId, "wrong-generation", default))).Code);
    }

    [TestMethod]
    public void ProcessPinsBothNativeConfigurationResolversAndSuppressesUpdater()
    {
        var profile = Path.Combine(_root, "private-profile"); var config = Path.Combine(profile, "terabox", "config.json");
        var start = TeraBoxCliProcess.CreateStartInfo("terabox", config, ["login", "--code-stdin"]);
        Assert.AreEqual(profile, start.Environment["XDG_CONFIG_HOME"]); Assert.AreEqual(profile, start.Environment["APPDATA"]);
        Assert.IsTrue(start.ArgumentList.Contains("--no-check-update")); Assert.IsTrue(start.ArgumentList.Contains("--json"));
        Assert.AreEqual(config, start.ArgumentList[start.ArgumentList.IndexOf("--config") + 1]);
        Assert.IsFalse(start.Environment.Keys.Any(k => k.StartsWith("TERABOX_", StringComparison.OrdinalIgnoreCase)));
        start.Environment.TryGetValue("HOME", out var home);
        Assert.AreEqual(Environment.GetEnvironmentVariable("HOME"), home);
    }

    [TestMethod]
    public async Task NasOutputPermissionsArePreservedWhileMetadataRemainsPrivate()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Unix permission assertion"); return; }
        await Login(); Directory.CreateDirectory(Request.OutputDirectory);
        var chosen = (UnixFileMode)0x1ed;
        File.SetUnixFileMode(Request.OutputDirectory, chosen);
        await Download();
        Assert.AreEqual(chosen, File.GetUnixFileMode(Request.OutputDirectory));
        Assert.AreEqual((UnixFileMode)0x1c0, File.GetUnixFileMode(Path.Combine(_accounts.Root, "downloads")));
        Assert.AreEqual((UnixFileMode)0x180, File.GetUnixFileMode(Directory.EnumerateFiles(Path.Combine(_accounts.Root, "downloads")).Single()));
        var temp = Path.Combine(_root, "nas", "native", ".temporary"); Directory.CreateDirectory(temp); File.SetUnixFileMode(temp, chosen);
        TeraBoxCliProcess.CreateStartInfo("terabox", Path.Combine(_root, "profile", "terabox", "config.json"),
            ["share-download", Source.Url], Path.GetDirectoryName(temp));
        Assert.AreEqual(chosen, File.GetUnixFileMode(temp));
    }

    [TestMethod]
    public void PayloadDirectoryCreationRejectsSymlinkAncestorsWithoutWritingThroughThem()
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Requires unprivileged symlink creation"); return; }
        var target = Path.Combine(_root, "actual"); Directory.CreateDirectory(target);
        var link = Path.Combine(_root, "link"); Directory.CreateSymbolicLink(link, target);
        var error = Assert.ThrowsExactly<TeraBoxException>(() => TeraBoxStorage.PayloadDirectory(Path.Combine(link, "new")));
        Assert.AreEqual("TERABOX_LOCAL_PATH_INVALID", error.Code);
        Assert.IsFalse(Directory.Exists(Path.Combine(target, "new")));
    }

    [DataTestMethod]
    [DataRow("{\"errno\":\"secret\",\"message\":\"private-canary\"}")]
    [DataRow("{\"errno\":null}")]
    [DataRow("{\"errno\":{},\"error\":\"private-canary\"}")]
    public void MalformedProviderErrorNeverThrowsOrLeaksDetails(string json) =>
        Assert.AreEqual("TERABOX_CLI_REQUEST_FAILED", TeraBoxCliProcess.Classify(J(json)));

    [TestMethod]
    public void OfficialMetadataDiagnosticsAreDiscardedWithoutExposingSignedApiResponses()
    {
        var output = "[DEBUG] shorturlinfo response: {\"sign\":\"signature-canary\",\"randsk\":\"cookie-canary\"}\n\n" +
                     "{\n  \"share_id\": 12,\n  \"uk\": 34,\n  \"status\": 0\n}";
        var result = TeraBoxCliProcess.ParseResponse("share-info", output);
        Assert.AreEqual(0, result.GetProperty("status").GetInt32());
        Assert.IsFalse(result.GetRawText().Contains("canary")); Assert.AreEqual(3, result.EnumerateObject().Count());
        var listed = TeraBoxCliProcess.ParseResponse("share-list",
            "[DEBUG] shorturlinfo response: {\"randsk\":\"cookie-canary\"}\r\n" +
            "[DEBUG] share/list params: shorturl=1fixture, sekey=cookie-canary, dir=/folder, root=0, page=2, num=100\r\n" +
            "[DEBUG] share/list response: {\"errno\":0,\"list\":[{\"dlink\":\"https://invalid.example/?token=secret-canary\"}]}\r\n\r\n" +
            "{\"files\":[],\"total\":0,\"has_more\":false}");
        Assert.IsFalse(listed.GetRawText().Contains("canary")); Assert.AreEqual(0, listed.GetProperty("total").GetInt32());
    }

    [TestMethod]
    public void OfficialPayloadParameterAndJsonDiagnosticsDoNotBecomeDownloadReceipt()
    {
        var result = TeraBoxCliProcess.ParseResponse("share-download",
            "[DEBUG] shorturlinfo response: {\"sign\":\"signature-canary\"}\n" +
            "[DEBUG] share/download URL params (no token): shareid=12&uk=34&sign=signature-canary&timestamp=1700000000&fid_list=[56]&sekey=cookie-canary+/%=\n" +
            "[DEBUG] share/download response: {\"errno\":0,\"dlink\":\"https://invalid.example/?sign=signature-canary\"}\n" +
            "{\"success\":true,\"name\":\"fixture.bin\",\"local_path\":\"/nas/fixture.bin\",\"size\":3}");
        Assert.IsTrue(result.GetProperty("success").GetBoolean()); Assert.IsFalse(result.GetRawText().Contains("canary"));
    }

    [TestMethod]
    public async Task ActualCliDirectoryDiagnosticsPassThroughTheCompleteManifestPipeline()
    {
        await Login(); _native.MetadataDiagnostics = true; _native.MorePages = true;
        // Synthetic values reproduce actual 1.1.0 stdout: root=1 for the root,
        // root= (empty) on every --dir page, followed by the formal CLI object.
        var manifest = await _accounts.WithAccountAsync(null, null,
            account => TeraBoxCloudProvider.ReadManifest(account, Source, default), default);
        Assert.AreEqual(2, manifest.Count);
        CollectionAssert.AreEqual(new[] {"folder/a.bin", "folder/b.bin"}, manifest.Select(f => f.RelativePath).ToArray());
        CollectionAssert.AreEqual(new[] {"2", "3"}, manifest.Select(f => f.Id).ToArray());
        Assert.AreEqual(6L, manifest.Sum(f => f.Size));
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(0, _native.Downloads); Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
        Assert.IsFalse(_native.Calls.Any(c => c[0] is "share-download" or "share-save"));
    }

    [DataTestMethod]
    [DataRow("share-info", "[DEBUG] future response: {\"secret\":\"canary\"}\n{}")]
    [DataRow("share-info", "[DEBUG] shorturlinfo response: {\"secret\":\"canary\"\n{}")]
    [DataRow("share-info", "[DEBUG] shorturlinfo response: []\n{}")]
    [DataRow("share-info", "[DEBUG] shorturlinfo response: {}{}\n{}")]
    [DataRow("share-info", "[DEBUG] shorturlinfo response: {}\n{}\n{}")]
    [DataRow("share-info", "{\"status\":0}\nunknown canary text")]
    [DataRow("share-info", "[DEBUG] share/transfer response: {}\n{}")]
    [DataRow("share-list", "[DEBUG] verify: BOXCLND=\"canary\", randsk=\"canary\", cookies=[]\n{}")]
    [DataRow("whoami", "[DEBUG] shorturlinfo response: {}\n{}")]
    [DataRow("share-list", "[DEBUG] share/list params: shorturl=1fixture, sekey=canary, dir=/, root=2, page=1, num=100\n{}")]
    [DataRow("share-list", "[DEBUG] share/list params: shorturl=1fixture, sekey=canary, dir=/, root=1, num=100, page=1\n{}")]
    [DataRow("share-download", "[DEBUG] share/download URL params (no token): shareid=12&uk=34&sign=canary&timestamp=x&fid_list=[56]&sekey=canary\n{}")]
    public void UnsupportedOrIncompleteNativeStdoutFailsClosedWithoutLeakingDiagnostics(string command, string output)
    {
        var error = Assert.ThrowsExactly<TeraBoxException>(() => TeraBoxCliProcess.ParseResponse(command, output));
        Assert.AreEqual("TERABOX_CLI_RESPONSE_INVALID", error.Code); Assert.IsFalse(error.ToString().Contains("canary"));
    }

    [TestMethod]
    public void DiagnosticsHaveCountAndParameterLengthBounds()
    {
        Assert.ThrowsExactly<TeraBoxException>(() => TeraBoxCliProcess.ParseResponse("share-info",
            string.Concat(Enumerable.Repeat("[DEBUG] shorturlinfo response: {}\n", 17)) + "{}"));
        Assert.ThrowsExactly<TeraBoxException>(() => TeraBoxCliProcess.ParseResponse("share-list",
            "[DEBUG] share/list params: shorturl=1fixture, sekey=" + new string('a', 17000) + ", dir=/, root=1, page=1, num=100\n{}"));
    }

    [TestMethod]
    public async Task MetadataProtocolFailureRecoversSameOperationOnlyAfterACompleteReadOnlyManifestCheck()
    {
        await Login(); _native.InvalidMetadata = true;
        Assert.IsTrue(_provider.CanRevalidateDownloadFailure("TERABOX_CLI_RESPONSE_INVALID"));
        Assert.AreEqual("TERABOX_CLI_RESPONSE_INVALID", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(0, _native.Downloads); Assert.IsFalse(Directory.Exists(Path.Combine(_accounts.Root, "downloads")));
        Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
        _native.InvalidMetadata = false;
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(0, _native.Downloads); Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
        await Download(); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task ProtocolFailureAfterPayloadIsRetainedAndCannotBeRetriedAsEmptyMetadataFailure()
    {
        await Login(); _native.PayloadProtocolFailure = true;
        Assert.AreEqual("TERABOX_CLI_RESPONSE_INVALID", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await _provider.RevalidateDownloadAsync(Request, default)).Problem);
        _native.PayloadProtocolFailure = false;
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(Directory.EnumerateFiles(Request.OutputDirectory, "a.bin", SearchOption.AllDirectories).Single()));
    }

    [TestMethod]
    public async Task RecursivelyEnumeratesPaginatedFoldersAndDownloadsEveryLeafWithoutSavingShares()
    {
        await Login(); _native.MorePages = true;
        var result = await Download();
        Assert.AreEqual(2, result.Files.Count); Assert.AreEqual(2, _native.Downloads);
        Assert.IsTrue(File.Exists(Path.Combine(Request.OutputDirectory, "folder", "a.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(Request.OutputDirectory, "folder", "b.bin")));
        CollectionAssert.AreEqual(new[] {"2", "3"}, _native.Calls.Where(c => c[0] == "share-download").Select(c => c[Array.IndexOf(c, "--fsid") + 1]).ToArray());
        Assert.IsFalse(_native.Calls.Any(c => c[0] is "share-save" or "download"));
        Assert.IsFalse(Directory.EnumerateDirectories(Request.OutputDirectory).Any(p => Path.GetFileName(p).StartsWith(".bakabase")));
    }

    [TestMethod]
    public async Task CompletedReceiptReusesVerifiedFilesEvenIfOriginalShareExpires()
    {
        await Login(); await Download(); _native.ShareStatus = 1;
        var originalCalls = _native.Calls.Count;
        var result = await Download();
        Assert.AreEqual(1, result.Files.Count); Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual(originalCalls, _native.Calls.Count);
        File.WriteAllText(result.Files.Single(), "xyz");
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task ManifestChangeDuringPayloadNeverCommitsSuccessfulDelivery()
    {
        await Login(); _native.ChangeAfterPayload = true;
        Assert.AreEqual("TERABOX_SHARE_CHANGED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(1, _native.Downloads);
        Assert.IsTrue(File.Exists(Path.Combine(Request.OutputDirectory, "folder", "a.bin")));
        Assert.AreEqual("TERABOX_SHARE_CHANGED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(1, _native.Downloads);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VerifiedReceiptSurvivesCrashBeforeOrAfterLocalMoveWithoutRedownload(bool moved)
    {
        await Login(); var result = await Download();
        var receiptPath = Directory.EnumerateFiles(Path.Combine(_accounts.Root, "downloads")).Single();
        var receipt = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!;
        receipt["completed"] = false; receipt["files"]![0]!["state"] = "verified";
        await File.WriteAllTextAsync(receiptPath, receipt.ToJsonString());
        if (!moved)
        {
            var staging = Path.Combine(Request.OutputDirectory, ".bakabase-terabox-" + TeraBoxStorage.Hash(Request.OperationId)[..16], "2", "a.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(staging)!); File.Move(result.Files.Single(), staging);
        }
        await Download(); Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(result.Files.Single()));
    }

    [TestMethod]
    public async Task PartialPayloadIsPreservedAndNeverRedownloadedOrAbandoned()
    {
        await Login(); _native.FailPayload = true;
        Assert.AreEqual("TERABOX_NETWORK_UNAVAILABLE", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await _provider.RevalidateDownloadAsync(Request, default)).Problem);
        Assert.IsFalse((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        _native.FailPayload = false;
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual("a", await File.ReadAllTextAsync(Directory.EnumerateFiles(Request.OutputDirectory, "a.bin", SearchOption.AllDirectories).Single()));
    }

    [TestMethod]
    public async Task RejectedEmptyPayloadCanBeAbandonedWithoutRetryingOrChangingItsReceipt()
    {
        await Login(); _native.PayloadProblem = "TERABOX_DOWNLOAD_IDENTITY_REJECTED";
        Assert.AreEqual("TERABOX_DOWNLOAD_IDENTITY_REJECTED", (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.IsFalse(_provider.CanRevalidateDownloadFailure("TERABOX_DOWNLOAD_IDENTITY_REJECTED"));
        Assert.IsFalse(_provider.CanRevalidateDownloadFailure("TERABOX_DOWNLOAD_ACCESS_DENIED"));
        var receiptPath = Directory.EnumerateFiles(Path.Combine(_accounts.Root, "downloads")).Single();
        var receipt = await File.ReadAllTextAsync(receiptPath);
        StringAssert.Contains(receipt, "submitted");
        var calls = _native.Calls.Count;
        Assert.IsTrue((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        Assert.AreEqual(calls, _native.Calls.Count, "Abandonment does not require a new token or a network request.");
        Assert.AreEqual(receipt, await File.ReadAllTextAsync(receiptPath));
        Assert.AreEqual(1, _native.Downloads);
        Assert.AreEqual(0, Directory.EnumerateFiles(Request.OutputDirectory, "*", SearchOption.AllDirectories).Count());
    }

    [DataTestMethod]
    [DataRow("verified")]
    [DataRow("unknown")]
    [DataRow("completed")]
    [DataRow("hash")]
    [DataRow("destination")]
    public async Task EmptyPayloadDoesNotPermitAbandonmentOfCompletedUncertainOrRetainedReceipts(string change)
    {
        await Login(); _native.PayloadProblem = "TERABOX_DOWNLOAD_IDENTITY_REJECTED";
        await Assert.ThrowsExactlyAsync<CloudProviderException>(Download);
        var receiptPath = Directory.EnumerateFiles(Path.Combine(_accounts.Root, "downloads")).Single();
        var receipt = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!;
        if (change is "verified" or "unknown") receipt["files"]![0]!["state"] = change;
        if (change == "completed") receipt["completed"] = true;
        if (change == "hash") receipt["files"]![0]!["sha256"] = "retained-proof";
        if (change == "destination")
        {
            var destination = Path.Combine(Request.OutputDirectory, "folder", "a.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, "keep");
        }
        await File.WriteAllTextAsync(receiptPath, receipt.ToJsonString());
        var calls = _native.Calls.Count;
        Assert.AreEqual("TERABOX_DOWNLOAD_REVIEW_REQUIRED", (await _provider.CheckCanAbandonAsync(Request, default)).Problem);
        Assert.AreEqual(calls, _native.Calls.Count);
        Assert.AreEqual(receipt.ToJsonString(), await File.ReadAllTextAsync(receiptPath));
        if (change == "destination") Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(Request.OutputDirectory, "folder", "a.bin")));
    }

    [TestMethod]
    public async Task MetadataFailureAndEmptyFailedDownloadCanBeRevalidatedWithoutRequestingPayload()
    {
        await Login(); _native.FailBeforePayload = true;
        await Assert.ThrowsExactlyAsync<CloudProviderException>(Download);
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(1, _native.Downloads);
        _native.FailBeforePayload = false;
        await Download(); Assert.AreEqual(2, _native.Downloads);
    }

    [DataTestMethod]
    [DataRow("traversal", "TERABOX_LOCAL_PATH_INVALID")]
    [DataRow("missing_more", "TERABOX_MANIFEST_INVALID")]
    [DataRow("repeat_page", "TERABOX_MANIFEST_CHANGED")]
    [DataRow("wrong_parent", "TERABOX_MANIFEST_CHANGED")]
    [DataRow("size_unknown", "TERABOX_MANIFEST_INVALID")]
    public async Task IncompleteOrUnsafeManifestCannotStartAnyDownload(string mode, string expected)
    {
        await Login(); _native.ManifestMode = mode;
        Assert.AreEqual(expected, (await Assert.ThrowsExactlyAsync<CloudProviderException>(Download)).Message);
        Assert.AreEqual(0, _native.Downloads); Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
    }

    [DataTestMethod]
    [DataRow(0, "available", "TERABOX_SHARE_AVAILABLE")]
    [DataRow(1, "confirmedUnavailable", "TERABOX_SHARE_EXPIRED")]
    [DataRow(2, "confirmedUnavailable", "TERABOX_SHARE_DELETED")]
    [DataRow(9, "unknown", "TERABOX_SHARE_UNAVAILABLE")]
    public async Task OnlyAuthoritativeStatusMarksShareInvalid(int status, string expected, string reason)
    {
        var account = await Login(); _native.ShareStatus = status;
        var probe = new TeraBoxSourceAvailabilityProbe(_accounts);
        var result = await probe.CheckAsync(Source with {AccountId = account.AccountId, AccountVersion = account.AccountVersion}, default);
        Assert.AreEqual(expected, result.Status); Assert.AreEqual(reason, result.ReasonCode); Assert.AreEqual(0, _native.Downloads);
    }

    [DataTestMethod]
    [DataRow(0, "unknown", "TERABOX_MANIFEST_INVALID")]
    [DataRow(1, "confirmedUnavailable", "TERABOX_SHARE_EXPIRED")]
    [DataRow(2, "confirmedUnavailable", "TERABOX_SHARE_DELETED")]
    public async Task NativeUnavailableStatusDoesNotRequirePositiveShareIds(int status, string expected, string reason)
    {
        var account = await Login(); _native.ShareStatus = status; _native.ZeroShareIds = true;
        var result = await new TeraBoxSourceAvailabilityProbe(_accounts).CheckAsync(
            Source with {AccountId = account.AccountId, AccountVersion = account.AccountVersion}, default);
        Assert.AreEqual(expected, result.Status); Assert.AreEqual(reason, result.ReasonCode);
        Assert.IsFalse(_native.Calls.Any(c => c[0] is "share-list" or "share-download"));
    }

    [TestMethod]
    public void PasswordScopeUsesExplicitCorrectionAndOnlySupportedCommands()
    {
        var source = Source with {Url = Source.Url + "?pwd=original", AccessCode = "corrected"};
        CollectionAssert.AreEqual(new[] {"share-info", Source.Url}, TeraBoxCloudProvider.ShareArguments("share-info", source));
        CollectionAssert.AreEqual(new[] {"share-list", Source.Url, "--pwd", "corrected"}, TeraBoxCloudProvider.ShareArguments("share-list", source));
    }

    private static JsonElement J(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private sealed class Native : ITeraBoxCliProcess
    {
        internal List<string[]> Calls {get;} = [];
        internal string? LoginInput, ManifestMode, OnlineProblem, QuotaResponse, PayloadProblem;
        internal int Logins, Downloads, ShareStatus;
        internal bool FailAfterLogin, FailBeforeLogin, FailPayload, FailBeforePayload, MorePages, NotReady, CancelLogin, ChangeAfterPayload, ZeroShareIds, InvalidMetadata, PayloadProtocolFailure, MetadataDiagnostics;
        public Task EnsureReadyAsync(CancellationToken ct) => NotReady ? Task.FromException(new TeraBoxException("TERABOX_DEPENDENCY_REQUIRED")) : Task.CompletedTask;
        public async Task<JsonElement> RunAsync(string config, IReadOnlyList<string> arguments, CancellationToken ct, string? standardInput = null, string? payloadDirectory = null)
        {
            Calls.Add(arguments.ToArray());
            string? Value(string key) { var i = arguments.ToList().IndexOf(key); return i < 0 ? null : arguments[i + 1]; }
            JsonElement Metadata(JsonElement data)
            {
                if (!MetadataDiagnostics) return data;
                var output = "[DEBUG] shorturlinfo response: {\"errno\":0,\"randsk\":\"cookie-canary\"}\n\n";
                if (arguments[0] == "share-list")
                    output += "[DEBUG] share/list params: shorturl=1fixture, sekey=cookie-canary, dir=" + (Value("--dir") ?? "") +
                              ", root=" + (Value("--dir") == null ? "1" : "") + ", page=" + Value("--page") + ", num=" + Value("--num") + "\n" +
                              "[DEBUG] share/list response: {\"errno\":0,\"list\":[]}\n";
                return TeraBoxCliProcess.ParseResponse(arguments[0], output + data.GetRawText());
            }
            switch (arguments[0])
            {
                case "login":
                    Logins++; LoginInput = standardInput;
                    if (CancelLogin) throw new OperationCanceledException();
                    if (FailBeforeLogin) throw new TeraBoxException("TERABOX_NETWORK_UNAVAILABLE");
                    Directory.CreateDirectory(Path.GetDirectoryName(config)!); await File.WriteAllTextAsync(config, "{\"fixture\":true}", ct);
                    if (FailAfterLogin) throw new TeraBoxException("TERABOX_NETWORK_UNAVAILABLE");
                    return J("{}");
                case "whoami": return JsonSerializer.SerializeToElement(new {logged_in = File.Exists(config)});
                case "quota":
                    if (OnlineProblem != null) throw new TeraBoxException(OnlineProblem);
                    return J(QuotaResponse ?? "{\"total\":100,\"used\":10,\"free\":90,\"usage_percent\":10}");
                case "share-info":
                    if (InvalidMetadata) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
                    return Metadata(JsonSerializer.SerializeToElement(new {share_id = ZeroShareIds ? 0 : 11, uk = ZeroShareIds ? 0 : 12, status = ShareStatus}));
                case "share-list":
                    if (Value("--dir") == null) return Metadata(J("""{"files":[{"fs_id":1,"name":"folder","path":"/folder","size":0,"is_dir":true}],"total":1,"has_more":false}"""));
                    var page = Value("--page");
                    if (ManifestMode == "missing_more") return J("""{"files":[],"total":0}""");
                    if (ManifestMode == "size_unknown") return J("""{"files":[{"fs_id":2,"name":"a.bin","path":"/folder/a.bin","is_dir":false}],"total":1,"has_more":false}""");
                    var name = ManifestMode == "traversal" ? "../a.bin" : ChangeAfterPayload && Downloads > 0 ? "changed.bin" : MorePages && page == "2" ? "b.bin" : "a.bin";
                    return Metadata(JsonSerializer.SerializeToElement(new {files = new[] {new {fs_id = MorePages && page == "2" ? 3 : 2,
                        name, path = ManifestMode == "wrong_parent" ? "/other/" + name : "/folder/" + name, size = 3, is_dir = false}}, total = 1,
                        has_more = ManifestMode == "repeat_page" || MorePages && page == "1"}));
                case "share-download":
                    Downloads++;
                    if (PayloadProblem != null) throw new TeraBoxException(PayloadProblem);
                    if (FailBeforePayload) throw new TeraBoxException("TERABOX_NETWORK_UNAVAILABLE");
                    var output = arguments[2]; await File.WriteAllTextAsync(output, FailPayload ? "a" : "abc", ct);
                    if (PayloadProtocolFailure) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
                    if (FailPayload) throw new TeraBoxException("TERABOX_NETWORK_UNAVAILABLE");
                    return JsonSerializer.SerializeToElement(new {success = true, local_path = output, name = Path.GetFileName(output), size = 3});
                default: throw new InvalidOperationException("Unexpected fixture command");
            }
        }
    }
}
