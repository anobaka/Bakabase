using System.Diagnostics;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.PikPakCli;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PikPakCliTests
{
    private string _root = null!;
    private Native _native = null!;
    private PikPakCliAccountService _accounts = null!;
    private PikPakCliCloudProvider _provider = null!;
    private CloudSource Source => new() {Id = "s", ProviderId = "pikpak", MethodId = "pikpak.share",
        Url = "https://mypikpak.com/s/VP34KSLprDtv30ftAvk2FVfao2"};
    private CloudDownloadRequest Request => new("operation", Source, null, null, Path.Combine(_root, "nas", "output"));

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pikpak-cli-tests-" + Guid.NewGuid().ToString("N"));
        _native = new(); _accounts = new(Path.Combine(_root, "appdata"), _native); _provider = new(_accounts, null, _ => Task.CompletedTask);
    }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private async Task<PikPakCliAccountViewModel> Login()
    {
        var session = await _accounts.StartAsync(default);
        _native.Approved = true;
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        return await _accounts.GetAsync(default);
    }

    [TestMethod]
    public async Task MissingAccountDoesNotRunAProcessOrCreateState()
    {
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        Assert.AreEqual("PIKPAK_ACCOUNT_REQUIRED", (await _provider.CheckReadyAsync(null, default)).Problem);
        Assert.AreEqual(0, _native.Calls.Count); Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task DeviceChallengeIsPrivateAndNoProfileIsAdoptedUntilApproval()
    {
        var session = await _accounts.StartAsync(default);
        Assert.AreEqual("pending", session.Status);
        var request = _native.Calls.Single();
        Assert.AreEqual("account,files:write", request[Array.IndexOf(request, "--scope") + 1]);
        Assert.IsFalse(request.Any(a => a.Contains("files:manage") || a.Contains("files:purge")));
        Assert.IsFalse(JsonSerializer.Serialize(session).Contains("device-secret-canary"));
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        var polled = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("pending", polled.Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        await _accounts.CancelAsync(session.Id, default);
        _native.Approved = true;
        Assert.AreEqual("cancelled", (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task PendingAuthorizationCanBeRecoveredAfterPageOrServiceRestart()
    {
        Assert.IsNull(await _accounts.GetActiveAuthorizationAsync(default));
        var session = await _accounts.StartAsync(default);
        _accounts = new(Path.Combine(_root, "appdata"), _native);
        var restored = await _accounts.GetActiveAuthorizationAsync(default);
        Assert.AreEqual(session.Id, restored!.Id);
        Assert.AreEqual("pending", restored.Status);
        Assert.AreEqual(session.UserCode, restored.UserCode);
        Assert.AreEqual(1, _native.Calls.Count, "Reading the session must not start another authorization.");
        Assert.IsFalse(JsonSerializer.Serialize(restored).Contains("device-secret-canary"));
        _native.Approved = true;
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
    }

    [DataTestMethod]
    [DataRow("{}")]
    [DataRow("{\"status\":\"success\"}")]
    [DataRow("{\"status\":\"future-provider-status\"}")]
    public async Task UncertainContinuationPreservesTheChallengeUntilCredentialsAreReady(string response)
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = response;
        var pending = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("pending", pending.Status);
        Assert.AreEqual("PIKPAK_AUTHORIZATION_AWAITING_CREDENTIALS", pending.Problem);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        var saved = await ReadAuthSession(session.Id);
        Assert.AreEqual("device-secret-canary", saved.DeviceCode);
        await AllowNextPoll(saved);
        _native.Approved = true;
        var complete = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("authorized", complete.Status);
        Assert.IsNull(complete.Problem);
    }

    [TestMethod]
    public async Task OfficialSuccessWithoutAccountScopeAndCredentialsStopsWithAnActionableReason()
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = """{"status":"success","scopes":["files:write"]}""";
        var failed = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("failed", failed.Status);
        Assert.AreEqual("PIKPAK_ACCOUNT_SCOPE_REQUIRED", failed.Problem);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        var saved = await ReadAuthSession(session.Id);
        Assert.AreEqual("success", saved.LastProviderStatus);
        CollectionAssert.AreEqual(new[] {"files:write"}, saved.GrantedScopes!);
        Assert.AreEqual("", saved.DeviceCode);
        var calls = _native.Calls.Count;
        await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual(calls, _native.Calls.Count, "A missing consent cannot be fixed by repeatedly exchanging its code.");
    }

    [DataTestMethod]
    [DataRow("{\"status\":\"success\"}")]
    [DataRow("{\"status\":\"success\",\"scopes\":\"files:write\"}")]
    [DataRow("{\"status\":\"success\",\"scopes\":[17]}")]
    [DataRow("{\"status\":\"success\",\"scopes\":[\"files:write\",\"unknown-private-canary\"]}")]
    [DataRow("{\"status\":\"success\",\"scopes\":[\"account\",\"files:write\"]}")]
    public async Task MissingOrUnsupportedScopeEvidenceCannotBeMisreportedAsMissingAccountPermission(string response)
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = response;
        var pending = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("pending", pending.Status);
        Assert.AreEqual("PIKPAK_AUTHORIZATION_AWAITING_CREDENTIALS", pending.Problem);
        Assert.AreEqual("device-secret-canary", (await ReadAuthSession(session.Id)).DeviceCode);
        Assert.IsFalse(JsonSerializer.Serialize(await ReadAuthSession(session.Id)).Contains("unknown-private-canary"));
    }

    [TestMethod]
    public async Task UsableOfficialCredentialsAreNotRejectedBecauseOfReportedScopes()
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = """{"status":"success","scopes":["files:write"]}""";
        _native.Approved = true;
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.IsTrue((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task SlowDownIsPendingAndBacksOffInsteadOfDiscardingTheDeviceCode()
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = "{\"status\":\"slow_down\"}";
        var pending = await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual("pending", pending.Status);
        Assert.AreEqual(7, pending.IntervalSeconds);
        var saved = await ReadAuthSession(session.Id);
        Assert.IsTrue(saved.NextPollAt > DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.AreEqual("device-secret-canary", saved.DeviceCode);
        Assert.IsFalse(_native.Calls.Any(c => c.SequenceEqual(new[] {"auth", "status"})));
        var calls = _native.Calls.Count;
        await _accounts.PollAsync(session.Id, default);
        Assert.AreEqual(calls, _native.Calls.Count, "Repeated browser requests must respect backoff.");
    }

    [DataTestMethod]
    [DataRow("pending", "credentials.json")]
    [DataRow("failed", "credentials.json")]
    [DataRow("pending", ".credentials.json")]
    [DataRow("failed", ".credentials.json")]
    public async Task ConsumedDeviceCodeWithValidCredentialsIsReconciledWithoutAnotherExchange(string state, string file)
    {
        var session = await _accounts.StartAsync(default);
        var saved = await ReadAuthSession(session.Id);
        saved.Status = state; saved.Problem = "PIKPAK_ACCOUNT_REQUIRED";
        if (state == "failed") saved.DeviceCode = "";
        await AllowNextPoll(saved);
        var profile = Path.Combine(_root, "appdata", "profiles", session.Id);
        Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, file), "credential-canary");
        _native.Approved = true;
        Assert.AreEqual("pending", (await _accounts.GetActiveAuthorizationAsync(default))!.Status);
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.IsFalse(_native.Calls.Any(c => c.Contains("--continue")));
        Assert.IsFalse(JsonSerializer.Serialize(await _accounts.GetAsync(default)).Contains("credential-canary"));
    }

    [DataTestMethod]
    [DataRow("denied", "failed")]
    [DataRow("expired", "expired")]
    public async Task ExplicitRejectionRemainsTerminal(string response, string expected)
    {
        var session = await _accounts.StartAsync(default);
        _native.Continuation = JsonSerializer.Serialize(new {status = response});
        Assert.AreEqual(expected, (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.AreEqual("", (await ReadAuthSession(session.Id)).DeviceCode);
        _native.Approved = true;
        Assert.AreEqual(expected, (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    private async Task<PikPakCliAccountService.Session> ReadAuthSession(string id) =>
        (await PikPakCliStorage.ReadAsync<PikPakCliAccountService.Session>(Path.Combine(_root, "appdata", "sessions", id + ".json"), default))!;

    private async Task AllowNextPoll(PikPakCliAccountService.Session session)
    {
        session.NextPollAt = DateTimeOffset.MinValue;
        await PikPakCliStorage.WriteAsync(Path.Combine(_root, "appdata", "sessions", session.Id + ".json"), session, default);
    }

    [TestMethod]
    public async Task UntrustedVerificationUrlCannotReachTheBrowser()
    {
        _native.VerificationUri = "https://attacker.invalid/?token=secret";
        var error = await Assert.ThrowsExceptionAsync<PikPakCliException>(() => _accounts.StartAsync(default));
        Assert.AreEqual("PIKPAK_AUTHORIZATION_RESPONSE_INVALID", error.Code);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task StartingAnotherChallengeSupersedesOldApprovalAndLogoutCancelsPending()
    {
        var old = await _accounts.StartAsync(default);
        var current = await _accounts.StartAsync(default);
        _native.Approved = true;
        Assert.AreEqual("cancelled", (await _accounts.PollAsync(old.Id, default)).Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        await _accounts.LogoutAsync(default);
        Assert.AreEqual("cancelled", (await _accounts.PollAsync(current.Id, default)).Status);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task ActiveAccountLeaseDoesNotBlockUiAndPreventsCredentialReplacement()
    {
        await Login();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = _accounts.WithAccountAsync(null, null, async _ => { entered.SetResult(); await release.Task; return true; }, default);
        await entered.Task;
        try
        {
            Assert.IsTrue((await _accounts.GetAsync(default).WaitAsync(TimeSpan.FromSeconds(1))).IsConfigured);
            var logout = await Assert.ThrowsExceptionAsync<PikPakCliException>(() => _accounts.LogoutAsync(default));
            Assert.AreEqual("PIKPAK_ACCOUNT_IN_USE", logout.Code);
            var start = await Assert.ThrowsExceptionAsync<PikPakCliException>(() => _accounts.StartAsync(default));
            Assert.AreEqual("PIKPAK_ACCOUNT_IN_USE", start.Code);
        }
        finally { release.SetResult(); await work; }
    }

    [TestMethod]
    public async Task InterruptedPromotionCannotEraseTheActiveProfileOnCancellation()
    {
        var session = await _accounts.StartAsync(default);
        var profile = Path.Combine(_root, "appdata", "profiles", session.Id);
        Directory.CreateDirectory(profile);
        await File.WriteAllTextAsync(Path.Combine(profile, "credentials.json"), "active-token-canary");
        await PikPakCliStorage.WriteAsync(Path.Combine(_root, "appdata", "account.json"),
            new PikPakCliAccountService.Account(session.Id, Guid.NewGuid().ToString("N"), "Fixture"), default);
        await _accounts.CancelAsync(session.Id, default);
        Assert.AreEqual("authorized", (await _accounts.PollAsync(session.Id, default)).Status);
        Assert.AreEqual("active-token-canary", await File.ReadAllTextAsync(Path.Combine(profile, "credentials.json")));
    }

    [TestMethod]
    public async Task AuthorizedProfileHasAnOpaqueGenerationAndSurvivesServiceRestart()
    {
        var account = await Login();
        Assert.IsTrue(Guid.TryParseExact(account.AccountId, "N", out _));
        Assert.IsTrue(Guid.TryParseExact(account.AccountVersion, "N", out _));
        _accounts = new(Path.Combine(_root, "appdata"), _native);
        Assert.AreEqual(account.AccountVersion, (await _accounts.GetAsync(default)).AccountVersion);
        Assert.IsFalse(JsonSerializer.Serialize(account).Contains("device-secret-canary"));
    }

    [TestMethod]
    public void ChildEnvironmentIsIsolatedWithoutChangingHome()
    {
        var before = Environment.GetEnvironmentVariable("HOME");
        var start = PikPakCliProcess.CreateStartInfo("pikpak", Path.Combine(_root, "profile"), ["auth", "status"]);
        start.Environment.TryGetValue("HOME", out var childHome);
        Assert.AreEqual(before, childHome);
        Assert.IsFalse(start.UseShellExecute);
        Assert.AreEqual("1", start.Environment["PIKPAK_NO_LOG"]);
        Assert.AreEqual("1", start.Environment["PIKPAK_NO_SKILL_SYNC"]);
        Assert.AreEqual("1", start.Environment["PIKPAK_NO_UPDATE_NOTIFIER"]);
        Assert.AreEqual(Path.Combine(_root, "profile", "credentials.json"), start.Environment["PIKPAK_CREDENTIALS"]);
        Assert.IsFalse(start.Environment.ContainsKey("PIKPAK_CREDENTIALS_PATH"));
        Assert.IsFalse(start.Environment.ContainsKey("PIKPAK_TOKEN"));
    }

    [TestMethod]
    public async Task ExistingIsolatedDottedCredentialsRemainUsableAndLogoutRemovesThem()
    {
        var account = await Login();
        var profile = Path.Combine(_root, "appdata", "profiles", account.AccountId!);
        Directory.CreateDirectory(profile);
        var legacy = Path.Combine(profile, ".credentials.json");
        await File.WriteAllTextAsync(legacy, "old-credential-canary");
        var start = PikPakCliProcess.CreateStartInfo("pikpak", profile, ["auth", "status"]);
        Assert.AreEqual(legacy, start.Environment["PIKPAK_CREDENTIALS"]);
        await _accounts.LogoutAsync(default);
        Assert.IsFalse(File.Exists(legacy));
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
    }

    [TestMethod]
    public async Task AmbiguousCredentialFilesArePreservedAndNeverSelectedArbitrarily()
    {
        var profile = Path.Combine(_root, "profile"); Directory.CreateDirectory(profile);
        foreach (var name in new[] {"credentials.json", ".credentials.json"})
            await File.WriteAllTextAsync(Path.Combine(profile, name), name);
        var error = Assert.ThrowsException<PikPakCliException>(() => PikPakCliProcess.CreateStartInfo("pikpak", profile, ["auth", "status"]));
        Assert.AreEqual("PIKPAK_STATE_INVALID", error.Code);
        Assert.AreEqual(2, Directory.GetFiles(profile).Length);
    }

    [TestMethod]
    public void AccountAndAuthorizationActionsAreNoStoreEvenOnSuccessfulOwnerRequests()
    {
        var filter = (OwnerUiOriginAttribute)Attribute.GetCustomAttribute(typeof(PikPakCliController), typeof(OwnerUiOriginAttribute))!;
        var http = new DefaultHttpContext();
        http.Request.Scheme = "http"; http.Request.Host = new("127.0.0.1:34567");
        http.Request.Headers.Origin = "http://127.0.0.1:34567";
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        filter.OnAuthorization(context);
        Assert.IsNull(context.Result);
        Assert.AreEqual("no-store", http.Response.Headers.CacheControl.ToString());
    }

    [TestMethod]
    public void DownloadTemporaryFilesStayOnTheDestinationStorage()
    {
        var profile = Path.Combine(_root, "appdata", "profile");
        var stage = Path.Combine(_root, "nas", "stage");
        string[] args = ["download", "id", "--output", Path.Combine(stage, "file.payload")];
        var start = PikPakCliProcess.CreateStartInfo("pikpak", profile, args);
        PikPakCliProcess.ConfigurePayloadStorage(start, args);
        Assert.AreEqual(stage, start.WorkingDirectory);
        foreach (var key in new[] {"TMPDIR", "TMP", "TEMP"}) Assert.AreEqual(Path.Combine(stage, "temporary"), start.Environment[key]);
        Assert.AreEqual(profile, start.Environment["PIKPAK_DIR"]);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "appdata")));
    }

    [TestMethod]
    public async Task RecoveryListsButNeverSavesOrDownloads()
    {
        await Login();
        var ready = await _provider.RevalidateDownloadAsync(Request, default);
        Assert.IsTrue(ready.Available, ready.Problem);
        Assert.AreEqual(0, _native.Restores); Assert.AreEqual(0, _native.Downloads); Assert.AreEqual(0, _native.Mkdirs);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "appdata", "operations")));
        Assert.IsFalse(Directory.Exists(Request.OutputDirectory));
    }

    [TestMethod]
    public async Task ShareRestoreDownloadAndVerifiedReuseDoNotRepeatCloudWritesOrPayloads()
    {
        await Login();
        var first = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual("abc", await File.ReadAllTextAsync(first.Files.Single()));
        Assert.IsTrue(first.Files.Single().StartsWith(Request.OutputDirectory));
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Mkdirs); Assert.AreEqual(1, _native.Downloads);
        var second = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        CollectionAssert.AreEqual(first.Files.ToArray(), second.Files.ToArray());
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
        Assert.IsFalse(Directory.EnumerateDirectories(Request.OutputDirectory).Any(d => Path.GetFileName(d).StartsWith(".bakabase-pikpak-")));
    }

    [TestMethod]
    public async Task AVerifiedSavedCopySurvivesExpiryOfTheOriginalShare()
    {
        await Login();
        var files = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        _native.OriginalExpired = true;
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        var reused = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        CollectionAssert.AreEqual(files.Files.ToArray(), reused.Files.ToArray());
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task UnknownRestoreReconcilesTheOwnedDestinationInsteadOfRepeating()
    {
        await Login(); _native.FailAfterRestore = true;
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_NETWORK_UNAVAILABLE", error.Message);
        _provider = new(_accounts, null, _ => Task.CompletedTask);
        var result = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, result.Files.Count); Assert.AreEqual(1, _native.Restores);
    }

    [TestMethod]
    public async Task UnknownEmptyRestoreRequiresReviewAndNeverRepeats()
    {
        await Login(); _native.FailBeforeRestoreCommit = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        var again = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_RESTORE_REVIEW_REQUIRED", again.Message); Assert.AreEqual(1, _native.Restores); Assert.AreEqual(0, _native.Downloads);
        var before = await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json", SearchOption.AllDirectories).Single());
        Assert.IsFalse((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        _native.CompleteRestore();
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(before, await File.ReadAllTextAsync(Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json", SearchOption.AllDirectories).Single()), "Diagnosis cannot mutate the transfer journal.");
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task APermissionErrorAfterSubmissionCannotCauseAnUnverifiedRestoreRepeat()
    {
        await Login(); _native.RejectRestorePermission = true;
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_PERMISSION_REQUIRED", error.Message);
        Assert.AreEqual(0, _native.SuccessfulRestores);
        var retry = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_RESTORE_REVIEW_REQUIRED", retry.Message);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Mkdirs); Assert.AreEqual(0, _native.SuccessfulRestores); Assert.AreEqual(0, _native.Downloads);
    }

    [TestMethod]
    public async Task UnrelatedFilesInAnOwnedFolderAreNotAdopted()
    {
        await Login(); _native.ExtraRestoredFile = true;
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_RESTORE_PENDING", error.Message);
        Assert.IsFalse((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(0, _native.Downloads);
    }

    [TestMethod]
    public async Task RemoteTreeMismatchDoesNotPublishOrRepeatRestore()
    {
        await Login(); _native.WrongRestoredName = true;
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_RESTORE_PENDING", error.Message);
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(0, _native.Downloads);
    }

    [TestMethod]
    public async Task PartialFileIsPreservedAndNotDownloadedAgain()
    {
        await Login(); _native.FailPayload = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        var partial = Directory.GetFiles(Request.OutputDirectory, "*.payload", SearchOption.AllDirectories).Single();
        Assert.AreEqual("a", await File.ReadAllTextAsync(partial));
        var again = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED", again.Message);
        Assert.AreEqual(1, _native.Downloads); Assert.AreEqual("a", await File.ReadAllTextAsync(partial));
    }

    [TestMethod]
    public async Task EditedCompletedFileIsKeptAndNeverOverwrittenOrRedownloaded()
    {
        await Login();
        var files = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        await File.WriteAllTextAsync(files.Files.Single(), "xyz");
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_LOCAL_CHANGED", error.Message);
        Assert.AreEqual("xyz", await File.ReadAllTextAsync(files.Files.Single())); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task AccountReplacementCannotReuseTheOldAccountRestoreJournal()
    {
        await Login();
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        await Login();
        var error = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_ACCOUNT_CHANGED", error.Message);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task SelectingAChildChecksAncestryAndRestoresOnlyThatChild()
    {
        await Login();
        var selected = Source with {Url = Source.Url + "/AAAAKXTK1uoYBDiXHGT7xH2to2_VP3"};
        var ready = await _provider.RevalidateDownloadAsync(Request with {Source = selected}, default);
        Assert.IsTrue(ready.Available, ready.Problem);
        var missing = selected with {Url = Source.Url + "/VP34AAAAAAAAAAAAAAAAAAAAAA"};
        var blocked = await _provider.RevalidateDownloadAsync(Request with {Source = missing}, default);
        Assert.IsFalse(blocked.Available); Assert.AreEqual("PIKPAK_SELECTED_PATH_INVALID", blocked.Problem);
    }

    [TestMethod]
    public async Task RepeatedPageTokenCannotProduceAnIncompleteManifest()
    {
        await Login(); _native.RepeatToken = true;
        var ready = await _provider.RevalidateDownloadAsync(Request, default);
        Assert.IsFalse(ready.Available); Assert.AreEqual("PIKPAK_SHARE_MANIFEST_INVALID", ready.Problem);
        Assert.AreEqual(0, _native.Restores);
    }

    [TestMethod]
    public async Task LogoutKeepsJournalAndDownloadedFiles()
    {
        await Login();
        var files = await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        await _accounts.LogoutAsync(default);
        Assert.IsFalse((await _accounts.GetAsync(default)).IsConfigured);
        Assert.IsTrue(File.Exists(files.Files.Single()));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json", SearchOption.AllDirectories).Length);
    }

    [DataTestMethod]
    [DataRow("UNAUTHENTICATED", "", "PIKPAK_ACCOUNT_REQUIRED")]
    [DataRow("ACCESS_DENIED", "", "PIKPAK_PERMISSION_REQUIRED")]
    [DataRow("REQUEST_FAILED", "storage quota exceeded", "PIKPAK_STORAGE_QUOTA_EXCEEDED")]
    [DataRow("RATE_LIMITED", "", "PIKPAK_TRANSFER_QUOTA_EXCEEDED")]
    [DataRow("SHARE_EXPIRED", "", "PIKPAK_SHARE_EXPIRED")]
    [DataRow("REQUEST_FAILED", "unknown failure https://secret.invalid/token", "PIKPAK_CLI_REQUEST_FAILED")]
    public void FailuresRemainDistinctAndNeverExposeProviderDiagnostics(string code, string reason, string expected)
    {
        var json = JsonSerializer.SerializeToElement(new {error = new {code, reason}});
        Assert.AreEqual(expected, PikPakCliProcess.Classify(json));
    }

    [DataTestMethod]
    [DataRow("authorization_pending", "PIKPAK_AUTHORIZATION_PENDING")]
    [DataRow("slow_down", "PIKPAK_AUTHORIZATION_SLOW_DOWN")]
    [DataRow("expired_token", "PIKPAK_AUTHORIZATION_EXPIRED")]
    [DataRow("access_denied", "PIKPAK_AUTHORIZATION_DENIED")]
    public void OAuthContinuationErrorsDoNotChangeDownloadErrorClassification(string code, string expected)
    {
        var json = JsonSerializer.SerializeToElement(new {error = new {code}});
        Assert.AreEqual(expected, PikPakCliProcess.Classify(json, authorization: true));
        Assert.AreEqual("PIKPAK_CLI_REQUEST_FAILED", PikPakCliProcess.Classify(json));
    }

    [DataTestMethod]
    [DataRow("3.28 GB", 3525596184L, true)]
    [DataRow("3 B", 3L, true)]
    [DataRow("3 B", 4L, false)]
    [DataRow("3.28 GB", 8000000000L, false)]
    [DataRow("-", 0L, false)]
    public void RoundedShareSizesAreBoundsNotInventedExactSizes(string display, long bytes, bool expected) =>
        Assert.AreEqual(expected, PikPakCliCloudProvider.DisplaySizeMatches(display, bytes));

    [DataTestMethod]
    [DataRow("40.43 GB", "6.00 GB", "3.28 GB", "insufficient")]
    [DataRow("6.00 GB", "6.00 GB", "3 B", "notProvenInsufficient")]
    [DataRow("5.99 GB", "6.00 GB", "0.01 GB", "notProvenInsufficient")]
    [DataRow("5.99 GB", "6.00 GB", "1.00 GB", "insufficient")]
    [DataRow("2 B", "4 B", "3 B", "insufficient")]
    [DataRow("1 B", "4 B", "3 B", "notProvenInsufficient")]
    [DataRow("unknown", "6.00 GB", "3 B", "unknown")]
    [DataRow("1 B", "0 B", "3 B", "unknown")]
    [DataRow("1 B", "4 B", "-", "unknown")]
    [DataRow("40.43 GB", "6.00 GB", "-", "insufficient")]
    public void CapacityUsesConservativeBoundsAndDistinguishesUnknown(string used, string total, string required, string expected)
    {
        var status = JsonSerializer.SerializeToElement(new {storage = new {used, total}});
        Assert.AreEqual(expected, PikPakCliCapacity.Evaluate(status, [required]).Result);
    }

    [TestMethod]
    public async Task ClearingSpaceAfterAPreflightRejectionResumesTheSameOperationWithoutDuplicateWrites()
    {
        await Login(); _native.Storage = """{"used":"40.43 GB","total":"6.00 GB"}""";
        var first = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", first.Message);
        Assert.AreEqual(0, _native.Mkdirs); Assert.AreEqual(0, _native.Restores); Assert.AreEqual(0, _native.Downloads);
        Assert.IsTrue(_provider.CanRevalidateDownloadFailure(first.Message));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", (await _provider.RevalidateDownloadAsync(Request, default)).Problem);
        Assert.IsTrue((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        var journal = Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json", SearchOption.AllDirectories).Single();
        var before = await File.ReadAllTextAsync(journal);
        Assert.IsTrue(before.Contains("\"state\":\"planned\""));
        _native.Storage = """{"used":"0 B","total":"6.00 GB"}""";
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        Assert.AreEqual(before, await File.ReadAllTextAsync(journal), "Read-only recovery cannot rewrite quota history.");
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, _native.Mkdirs); Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
        Assert.IsFalse((await _provider.CheckCanAbandonAsync(Request, default)).Available);
    }

    [TestMethod]
    public async Task SpaceExhaustedAfterFolderCreationDoesNotSubmitAndReusesTheSameFolderAfterCleanup()
    {
        await Login(); _native.Storage = """{"used":"0 B","total":"4 B"}""";
        _native.AfterMkdir = () => _native.Storage = """{"used":"3 B","total":"4 B"}""";
        var failure = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", failure.Message);
        Assert.AreEqual(1, _native.Mkdirs); Assert.AreEqual(0, _native.Restores);
        Assert.IsFalse((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        _native.Storage = """{"used":"0 B","total":"4 B"}""";
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, _native.Mkdirs); Assert.AreEqual(1, _native.Restores);
    }

    [TestMethod]
    public async Task AQuotaErrorAfterSubmissionRemainsUnknownEvenAfterSpaceIsCleared()
    {
        await Login(); _native.RejectRestoreQuota = true;
        var failure = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", failure.Message);
        var journal = Directory.GetFiles(Path.Combine(_root, "appdata", "operations"), "transfer.json", SearchOption.AllDirectories).Single();
        using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(journal)))
        {
            var attempt = saved.RootElement.GetProperty("attempts")[0];
            Assert.AreEqual("unknown", attempt.GetProperty("outcome").GetString());
            Assert.AreEqual("PIKPAK_STORAGE_QUOTA_EXCEEDED", attempt.GetProperty("problem").GetString());
            Assert.AreEqual("share.restore", attempt.GetProperty("evidence").GetProperty("phase").GetString());
        }
        // An older receipt has the same safety boundary even without new diagnostics.
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(journal))!;
        legacy.AsObject().Remove("attempts"); legacy.AsObject().Remove("capacityChecks");
        await File.WriteAllTextAsync(journal, legacy.ToJsonString());
        var before = await File.ReadAllTextAsync(journal);
        _native.Storage = """{"used":"0 B","total":"6.00 GB"}""";
        Assert.AreEqual("PIKPAK_RESTORE_REVIEW_REQUIRED", (await _provider.RevalidateDownloadAsync(Request, default)).Problem);
        Assert.AreEqual(before, await File.ReadAllTextAsync(journal));
        Assert.IsFalse((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        var retry = await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        Assert.AreEqual("PIKPAK_RESTORE_REVIEW_REQUIRED", retry.Message);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(0, _native.Downloads);
        _native.CompleteRestore();
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task CompleteSavedFilesCanBeReusedWhileTheAccountIsOverQuota()
    {
        await Login(); _native.FailAfterRestore = true;
        await Assert.ThrowsExceptionAsync<CloudProviderException>(() => _provider.DownloadAsync(Request, _ => Task.CompletedTask, default));
        _native.Storage = """{"used":"40.43 GB","total":"6.00 GB"}""";
        Assert.IsTrue((await _provider.RevalidateDownloadAsync(Request, default)).Available);
        await _provider.DownloadAsync(Request, _ => Task.CompletedTask, default);
        Assert.AreEqual(1, _native.Restores); Assert.AreEqual(1, _native.Downloads);
    }

    [TestMethod]
    public async Task SourceAbandonmentWithoutAnOperationReceiptDoesNotReadAnAccountOrCreateState()
    {
        Assert.IsTrue((await _provider.CheckCanAbandonAsync(Request, default)).Available);
        Assert.AreEqual(0, _native.Calls.Count); Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task AccountTransferTransactionsSerializeWithoutBlockingCachedAccountViews()
    {
        await Login();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _accounts.WithAccountAsync(null, null, account => _accounts.WithTransferAsync(account, async () =>
        { entered.SetResult(); await release.Task; return 1; }, default), default);
        await entered.Task;
        var secondEntered = false;
        using var cancelled = new CancellationTokenSource();
        var second = _accounts.WithAccountAsync(null, null, account => _accounts.WithTransferAsync(account,
            () => { secondEntered = true; return Task.FromResult(2); }, cancelled.Token), default);
        Assert.IsFalse(secondEntered);
        Assert.IsTrue((await _accounts.GetAsync(default).WaitAsync(TimeSpan.FromSeconds(1))).IsConfigured);
        cancelled.Cancel();
        try { await second; Assert.Fail("The cancelled waiter must not enter the transfer transaction."); }
        catch (OperationCanceledException) { Assert.IsTrue(cancelled.IsCancellationRequested); }
        var third = _accounts.WithAccountAsync(null, null, account => _accounts.WithTransferAsync(account,
            () => { secondEntered = true; return Task.FromResult(3); }, default), default);
        Assert.IsFalse(secondEntered, "A cancelled waiter cannot release another operation's transfer lock.");
        release.SetResult();
        Assert.AreEqual(1, await first); Assert.AreEqual(3, await third); Assert.IsTrue(secondEntered);
    }

    [TestMethod]
    public void RestoreFailureEvidenceNeverPersistsArbitraryBodiesOrClaimsRejection()
    {
        var response = JsonSerializer.SerializeToElement(new {error = new {code = "private-token-canary", reason = "credential-canary", body = "secret-canary"}});
        var evidence = PikPakCliProcess.FailureEvidence(["share", "restore", "private-share-canary"], 1, "json", response);
        Assert.AreEqual("share.restore", evidence.Phase); Assert.AreEqual(1, evidence.ExitCode); Assert.IsNull(evidence.ProviderCode);
        Assert.IsFalse(JsonSerializer.Serialize(evidence).Contains("canary"));
        Assert.IsFalse(JsonSerializer.Serialize(evidence).Contains("rejected"));
    }

    [DataTestMethod]
    [DataRow("Need Password", "PIKPAK_ACCESS_CODE_REQUIRED")]
    [DataRow("Wrong Password", "PIKPAK_ACCESS_CODE_REQUIRED")]
    [DataRow("PASS_CODE_EMPTY", "PIKPAK_ACCESS_CODE_REQUIRED")]
    [DataRow("Deleted", "PIKPAK_SHARE_EXPIRED")]
    [DataRow("DELETED", "PIKPAK_SHARE_EXPIRED")]
    [DataRow("Expired", "PIKPAK_SHARE_EXPIRED")]
    [DataRow("Not Found", "PIKPAK_SHARE_EXPIRED")]
    [DataRow("future-status", "PIKPAK_SHARE_UNAVAILABLE")]
    [DataRow("OK", null)]
    public void OfficialCliShareLabelsAreNormalizedWithoutGuessing(string status, string? expected) =>
        Assert.AreEqual(expected, PikPakCliCloudProvider.ShareStatusProblem(status));

    internal sealed class Native : IPikPakCliProcess
    {
        internal List<string[]> Calls { get; } = [];
        internal bool Approved, FailAfterRestore, FailBeforeRestoreCommit, FailPayload, WrongRestoredName, RepeatToken, RejectRestorePermission, RejectRestoreQuota, ExtraRestoredFile, OriginalExpired;
        internal int Restores, SuccessfulRestores, Downloads, Mkdirs;
        internal string VerificationUri = "https://mypikpak.com/drive/activate";
        internal string? Continuation;
        internal string? Storage;
        internal Action? AfterMkdir;
        private string? _folderName;
        private bool _restored;
        internal void CompleteRestore() => _restored = true;
        public async Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
            bool payload = false, Action? observeProgress = null)
        {
            Calls.Add(arguments.ToArray());
            string? Value(string key) { var i = arguments.ToList().IndexOf(key); return i < 0 ? null : arguments[i + 1]; }
            if (arguments.Take(2).SequenceEqual(new[] {"auth", "login"}))
            {
                if (Value("--continue") != null) return Continuation != null ? J(Continuation) :
                    JsonSerializer.SerializeToElement(new {status = Approved ? "success" : "pending"});
                return JsonSerializer.SerializeToElement(new {flow = "device", device_code = "device-secret-canary",
                    verification_uri = VerificationUri, user_code = "ABCD-EFGH", expires_in = 300, interval = 2});
            }
            if (arguments.Take(2).SequenceEqual(new[] {"auth", "status"})) return Storage == null ?
                JsonSerializer.SerializeToElement(new {logged_in = Approved, name = "Fixture"}) :
                JsonSerializer.SerializeToElement(new {logged_in = Approved, name = "Fixture", storage = J(Storage)});
            if (arguments.Take(2).SequenceEqual(new[] {"auth", "logout"})) { Approved = false; return J("{}"); }
            if (arguments.Take(2).SequenceEqual(new[] {"share", "get"}))
            {
                if (OriginalExpired) throw new PikPakCliException("PIKPAK_SHARE_EXPIRED");
                if (Value("--parent") == null) return JsonSerializer.SerializeToElement(new {status = "OK", file_list = new[]
                    {new {id = "VP34AFYjetF32396Hp3N0Sp3o2", name = "folder", kind = "folder", size = "-"}}, next_page_token = RepeatToken ? "repeat" : ""});
                return J("""{"files":[{"id":"shared-file","name":"file.bin","kind":"file","size":"3 B"}]}""");
            }
            if (arguments[0] == "mkdir") { Mkdirs++; _folderName = arguments[1]; AfterMkdir?.Invoke(); return J("""{"id":"owned-folder"}"""); }
            if (arguments.Take(2).SequenceEqual(new[] {"share", "restore"}))
            {
                Restores++;
                if (RejectRestoreQuota) throw new PikPakCliException("PIKPAK_STORAGE_QUOTA_EXCEEDED")
                    {Evidence = new("share.restore", 1, "json", null)};
                if (RejectRestorePermission) { RejectRestorePermission = false; throw new PikPakCliException("PIKPAK_PERMISSION_REQUIRED"); }
                if (FailBeforeRestoreCommit) throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
                _restored = true; SuccessfulRestores++;
                if (FailAfterRestore) { FailAfterRestore = false; throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE"); }
                return J("""{"restore_status":"COMPLETE","file_id":"restored-folder"}""");
            }
            if (arguments[0] == "ls")
            {
                var parent = Value("--parent");
                if (parent == null) return _folderName == null ? J("""{"files":[]}""") :
                    JsonSerializer.SerializeToElement(new {files = new[] {new {id = "owned-folder", name = _folderName, kind = "drive#folder"}}});
                if (!_restored) return J("""{"files":[]}""");
                if (parent == "owned-folder") return J("""{"files":[{"id":"restored-folder","name":"folder","kind":"drive#folder"}]}""");
                if (ExtraRestoredFile) return J("""{"files":[{"id":"restored-file","name":"file.bin","kind":"drive#file","size":"3","hash":"remote-hash","modified_time":"2026-10-27T00:00:00Z"},{"id":"unrelated","name":"other.bin","kind":"drive#file","size":"3"}]}""");
                return JsonSerializer.SerializeToElement(new {files = new[] {new {id = "restored-file", name = WrongRestoredName ? "other.bin" : "file.bin",
                    kind = "drive#file", size = "3", hash = "remote-hash", modified_time = "2026-10-27T00:00:00Z", phase = "PHASE_TYPE_COMPLETE"}}});
            }
            if (arguments[0] == "download")
            {
                Downloads++; await File.WriteAllTextAsync(Value("--output")!, FailPayload ? "a" : "abc", ct);
                if (FailPayload) throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE");
                return J("{}");
            }
            throw new InvalidOperationException("Unexpected fixture command: " + string.Join(' ', arguments));
        }
        private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    }
}
