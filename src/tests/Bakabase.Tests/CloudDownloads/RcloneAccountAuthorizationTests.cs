using System.Reflection;
using System.Net;
using Bakabase.Service.Components.OAuth;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Service.Components.CloudDownloads.Rclone;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class RcloneAccountAuthorizationTests
{
    private string _root = null!;
    private Profiles _profiles = null!;
    private RcloneAccountStore _accounts = null!;
    private NativeProcess _native = null!;
    private RcloneAuthorizationService _authorization = null!;
    private const string Token = """{"access_token":"access-canary-private","refresh_token":"refresh-canary-private","expiry":"2099-01-01T00:00:00Z"}""";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "rclone-authorization-" + Guid.NewGuid().ToString("N"));
        _profiles = new(); _accounts = new(Path.Combine(_root, "accounts"), _profiles);
        _native = new(); _authorization = NewAuthorization();
    }
    private RcloneAuthorizationService NewAuthorization() => new(Path.Combine(_root, "authorization"), _accounts, _native);
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [TestMethod]
    public async Task ListingAndResolvingDoNotCreateDirectoriesOrPerformNetworkValidation()
    {
        Assert.AreEqual(0, (await _accounts.ListAsync(null, default)).Count);
        Assert.IsFalse(Directory.Exists(_root));
        var account = await _accounts.SaveAsync(new() {Name = "account", ProviderId = "pikpak", Username = "user", Password = "password-canary"}, default);
        Assert.AreEqual(account.Id, await _accounts.ResolveAccountIdAsync("pikpak", null, default));
        Assert.AreEqual("generation-1", await _accounts.GetConfiguredProfileVersionAsync("pikpak", account.Id, default));
        Assert.AreEqual(0, _profiles.Validations);
        var serialized = JsonSerializer.Serialize(await _accounts.ListAsync(null, default));
        Assert.IsFalse(serialized.Contains("password-canary"));
        Assert.IsFalse((await File.ReadAllTextAsync(Path.Combine(_root, "accounts", "accounts.json"))).Contains("password-canary"));
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _accounts.ResolveAccountIdAsync("oneDrive", account.Id, default));
        await _accounts.ValidateAsync(account.Id, default);
        Assert.AreEqual(1, _profiles.Validations);
    }

    [TestMethod]
    public async Task RenamePreservesCredentialsAndDefaultsAreProviderScoped()
    {
        var first = await _accounts.SaveAsync(new() {Name = "first", ProviderId = "pikpak", Username = "user", Password = "password"}, default);
        var second = await _accounts.SaveAsync(new() {Name = "second", ProviderId = "pikpak", Username = "user2", Password = "password2"}, default);
        var oneDrive = await NewOneDrive();
        await _accounts.SaveAsync(new() {Id = first.Id, Name = "renamed", ProviderId = "pikpak"}, default);
        Assert.AreEqual(2, _profiles.PikPakConfigurations);
        await _accounts.SetDefaultAsync(second.Id, default);
        var list = await _accounts.ListAsync(null, default);
        Assert.IsFalse(list.Single(a => a.Id == first.Id).IsDefault);
        Assert.IsTrue(list.Single(a => a.Id == second.Id).IsDefault);
        Assert.IsTrue(list.Single(a => a.Id == oneDrive.Id).IsDefault);
        await _accounts.DeleteAsync(second.Id, default);
        Assert.AreEqual(first.Id, await _accounts.ResolveAccountIdAsync("pikpak", null, default));
    }

    [TestMethod]
    public async Task NativeQuestionsPublishOnlyAtCompletionAndSurviveServerServiceRestart()
    {
        var account = await NewOneDrive();
        _native.Results.Enqueue(Question("oauth-state", "config_token", "Run:\n\trclone authorize \"onedrive\"\nThen paste the result.", secretDefault: Token));
        var started = await _authorization.StartAsync(account.Id, default);
        Assert.AreEqual("awaitingInput", started.Status); Assert.AreEqual("rclone authorize \"onedrive\"", started.RemoteAuthorizeCommand);
        Assert.IsTrue(started.Question!.IsSecret); Assert.IsNull(started.Question.DefaultValue); Assert.AreEqual(0, started.Question.Examples.Count);
        Assert.IsFalse(JsonSerializer.Serialize(started).Contains("access-canary-private"));
        _native.Results.Enqueue(Question("drive-state", "config_type", "Choose a drive", exclusive: true));
        _authorization = NewAuthorization();
        var drive = await _authorization.AnswerAsync(started.Id, new() {QuestionId = started.Question.Id, Value = Token}, default);
        Assert.AreEqual(0, _profiles.OneDriveConfigurations);
        Assert.IsFalse((await File.ReadAllTextAsync(SessionPath(started.Id))).Contains("access-canary-private"));
        _native.Results.Enqueue(Json("""{"State":"","Error":""}"""));
        _native.Results.Enqueue(Json(JsonSerializer.Serialize(new {type = "onedrive", token = Token, drive_id = "drive", drive_type = "documentLibrary"})));
        var ready = await _authorization.AnswerAsync(started.Id, new() {QuestionId = drive.Question!.Id, Value = "onedrive"}, default);
        Assert.AreEqual("ready", ready.Status); Assert.IsNull(ready.Question);
        Assert.AreEqual(1, _profiles.OneDriveConfigurations);
        Assert.AreEqual("documentLibrary", _profiles.LastOneDrive!.DriveType);
        Assert.AreEqual(Token, _profiles.LastOneDrive.TokenJson);
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(SessionPath(started.Id))!, "rclone.conf")));
        var duplicate = await _authorization.AnswerAsync(started.Id, new() {QuestionId = drive.Question.Id, Value = "onedrive"}, default);
        Assert.AreEqual("ready", duplicate.Status); Assert.AreEqual(1, _profiles.OneDriveConfigurations);
    }

    [TestMethod]
    public async Task InvalidOrStaleAnswerDoesNotCallNativeProcessOrChangeExistingProfile()
    {
        var account = await NewOneDrive();
        _profiles.Configured.Add(new("oneDrive", account.Id));
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.AnswerAsync(session.Id,
            new() {QuestionId = "stale", Value = Token}, default));
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.AnswerAsync(session.Id,
            new() {QuestionId = session.Question!.Id, Value = "invalid-token"}, default));
        Assert.AreEqual(1, _native.Calls);
        Assert.IsTrue((await _accounts.ListAsync("oneDrive", default)).Single().IsConfigured);
        Assert.AreEqual(0, _profiles.OneDriveConfigurations);
    }

    [TestMethod]
    public async Task NetworkFailureRetainsQuestionAndAllowsExplicitRetry()
    {
        var account = await NewOneDrive();
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        _native.Failure = new RcloneRemoteException("RCLONE_REMOTE_FAILED");
        var failed = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = Token}, default);
        Assert.AreEqual("awaitingInput", failed.Status); Assert.AreEqual(session.Question.Id, failed.Question!.Id);
        Assert.AreEqual("RCLONE_REMOTE_FAILED", failed.Problem);
        _native.Results.Enqueue(Question("drive", "config_type", "Choose", exclusive: true));
        var retried = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question.Id, Value = Token}, default);
        Assert.AreEqual("config_type", retried.Question!.Name); Assert.IsNull(retried.Problem);
    }

    [TestMethod]
    public async Task NativeErrorTransitionRecoversToANewQuestionWithoutReplayingTheDriveAnswerOrLeakingErrorText()
    {
        var account = await NewOneDrive();
        _native.Results.Enqueue(DriveQuestion());
        var session = await _authorization.StartAsync(account.Id, default);
        _native.Results.Enqueue(Json("""{"State":"choose_type","Error":"Failed to query root for drive private-drive-id: invalidRequest: https://private.invalid/?token=private-canary","Option":null,"Result":""}"""));
        _native.Results.Enqueue(Question("choose_type_done", "config_type", "Choose connection type", exclusive: true));
        var recovered = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("config_type", recovered.Question!.Name); Assert.AreNotEqual(session.Question.Id, recovered.Question.Id);
        Assert.AreEqual("RCLONE_CONFIGURATION_REQUEST_INVALID", recovered.Problem);
        using (var continuation = JsonDocument.Parse(_native.Inputs.Last()))
        {
            Assert.AreEqual("choose_type", continuation.RootElement.GetProperty("opt").GetProperty("state").GetString());
            Assert.AreEqual("", continuation.RootElement.GetProperty("opt").GetProperty("result").GetString());
        }
        var saved = await File.ReadAllTextAsync(SessionPath(session.Id));
        foreach (var secret in new[] {"private-drive-id", "private.invalid", "private-canary", "Failed to query"})
        { Assert.IsFalse(saved.Contains(secret)); Assert.IsFalse(JsonSerializer.Serialize(recovered).Contains(secret)); }
        _native.Results.Enqueue(DriveQuestion());
        var retry = await _authorization.AnswerAsync(session.Id, new() {QuestionId = recovered.Question.Id, Value = "onedrive"}, default);
        Assert.AreEqual("config_driveid", retry.Question!.Name); Assert.IsNull(retry.Problem);
    }

    [TestMethod]
    public async Task InterruptedErrorRecoveryRetainsItsNativeStateAndExplicitRetryDoesNotResubmitTheOldDriveId()
    {
        var account = await NewOneDrive(); _native.Results.Enqueue(DriveQuestion());
        var session = await _authorization.StartAsync(account.Id, default);
        _native.Results.Enqueue(Json("""{"State":"choose_type","Error":"accessDenied: private upstream details","Option":null}"""));
        _native.FailOnCall = 3; _native.CallFailure = new("RCLONE_CONFIGURATION_TIMED_OUT");
        var failed = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("RCLONE_CONFIGURATION_TIMED_OUT", failed.Problem); Assert.AreEqual(session.Question.Id, failed.Question!.Id);
        _authorization = NewAuthorization();
        _native.Results.Enqueue(Question("choose_type_done", "config_type", "Choose", exclusive: true));
        var retried = await _authorization.AnswerAsync(session.Id, new() {QuestionId = failed.Question.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("config_type", retried.Question!.Name); Assert.AreEqual("RCLONE_CONFIGURATION_ACCESS_DENIED", retried.Problem);
        using var input = JsonDocument.Parse(_native.Inputs.Last());
        Assert.AreEqual("choose_type", input.RootElement.GetProperty("opt").GetProperty("state").GetString());
        Assert.AreEqual("", input.RootElement.GetProperty("opt").GetProperty("result").GetString());
    }

    [TestMethod]
    public async Task RepeatingNativeErrorTransitionsAreBoundedAndRemainExplicitlyRecoverable()
    {
        var account = await NewOneDrive(); _native.Results.Enqueue(DriveQuestion());
        var session = await _authorization.StartAsync(account.Id, default);
        var error = Json("""{"State":"choose_type","Error":"accessDenied: private details","Option":null}""");
        _native.Results.Enqueue(error); _native.Results.Enqueue(error);
        var failed = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("RCLONE_CONFIGURATION_RECOVERY_LIMIT", failed.Problem); Assert.AreEqual(3, _native.Calls);
        Assert.AreEqual(session.Question.Id, failed.Question!.Id);
        _native.Results.Enqueue(Question("choose_type_done", "config_type", "Choose", exclusive: true));
        var retried = await _authorization.AnswerAsync(session.Id, new() {QuestionId = failed.Question.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("config_type", retried.Question!.Name);
    }

    [TestMethod]
    public async Task NativeResultTransitionsUseOnlyTheReturnedPrivateResultAndAllowTheSameStateWithNewResults()
    {
        var account = await NewOneDrive(); _native.Results.Enqueue(DriveQuestion());
        var session = await _authorization.StartAsync(account.Id, default);
        _native.Results.Enqueue(Json("""{"State":"next","Error":"","Option":null,"Result":"private-next-result"}"""));
        _native.Results.Enqueue(Json("""{"State":"next","Error":"","Option":null,"Result":"private-second-result"}"""));
        _native.Results.Enqueue(Question("choose_type_done", "config_type", "Choose", exclusive: true));
        var next = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("config_type", next.Question!.Name); Assert.IsNull(next.Problem);
        foreach (var (index, expected) in new[] {(2, "private-next-result"), (3, "private-second-result")})
        {
            using var input = JsonDocument.Parse(_native.Inputs[index]);
            Assert.AreEqual(expected, input.RootElement.GetProperty("opt").GetProperty("result").GetString());
        }
        Assert.IsFalse(JsonSerializer.Serialize(next).Contains("private-next-result"));
        Assert.IsFalse((await File.ReadAllTextAsync(SessionPath(session.Id))).Contains("private-next-result"));
    }

    [TestMethod]
    public async Task ErrorRecoveryCannotPublishACredentialProfileWithoutReturningAnExplicitQuestion()
    {
        var account = await NewOneDrive(); _native.Results.Enqueue(DriveQuestion());
        var session = await _authorization.StartAsync(account.Id, default);
        _native.Results.Enqueue(Json("""{"State":"unexpected_finish","Error":"accessDenied: private error","Option":null}"""));
        _native.Results.Enqueue(Json("""{"State":"","Error":"","Option":null}"""));
        var failed = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question!.Id, Value = "b!valid-drive"}, default);
        Assert.AreEqual("awaitingInput", failed.Status); Assert.AreEqual("RCLONE_CONFIGURATION_ACCESS_DENIED", failed.Problem);
        Assert.AreEqual(0, _profiles.OneDriveConfigurations);
        Assert.AreEqual(session.Question.Id, failed.Question!.Id);
    }

    [DataTestMethod]
    [DataRow(true, "true")]
    [DataRow(false, "false")]
    public async Task NativeBooleanDefaultsMatchTheirExactConfirmationOptions(bool defaultValue, string expected)
    {
        var account = await NewOneDrive();
        _native.Results.Enqueue(Json(JsonSerializer.Serialize(new {State = "driveid_final_end", Error = "", Option = new
        {Name = "config_drive_ok", Help = "Drive OK?", Type = "bool", Default = defaultValue, Exclusive = true,
            Examples = new[] {new {Value = "true", Help = "Yes"}, new {Value = "false", Help = "No"}}}})));
        var session = await _authorization.StartAsync(account.Id, default);
        Assert.AreEqual(expected, session.Question!.DefaultValue);
        _native.Results.Enqueue(DriveQuestion());
        var next = await _authorization.AnswerAsync(session.Id, new() {QuestionId = session.Question.Id, Value = session.Question.DefaultValue!}, default);
        Assert.AreEqual("config_driveid", next.Question!.Name);
        using var input = JsonDocument.Parse(_native.Inputs.Last());
        Assert.AreEqual(expected, input.RootElement.GetProperty("opt").GetProperty("result").GetString());
    }

    private static JsonElement DriveQuestion() => Json("""{"State":"driveid_final","Error":"","Option":{"Name":"config_driveid","Help":"Select drive you want to use","Type":"string","Required":true,"Exclusive":false,"Default":"b!valid-drive","Examples":[{"Value":"b!valid-drive","Help":"Documents (business)"},{"Value":"personal-drive","Help":"Personal (personal)"}]}}""");

    [TestMethod]
    public async Task NewAuthorizationCancelsOldSessionAndCancelRemovesOnlyPrivateStaging()
    {
        var account = await NewOneDrive();
        _native.Results.Enqueue(Question("one", "config_token", "rclone authorize \"onedrive\""));
        var first = await _authorization.StartAsync(account.Id, default);
        _native.Results.Enqueue(Question("two", "config_token", "rclone authorize \"onedrive\""));
        var second = await _authorization.StartAsync(account.Id, default);
        Assert.AreEqual("cancelled", (await _authorization.GetAsync(first.Id, default)).Status);
        await _authorization.CancelAsync(second.Id, default);
        Assert.AreEqual("cancelled", (await _authorization.GetAsync(second.Id, default)).Status);
        Assert.IsTrue(Directory.EnumerateFiles(Path.GetDirectoryName(SessionPath(second.Id))!).All(p => Path.GetFileName(p) == "session.json"));
        Assert.AreEqual(1, (await _accounts.ListAsync("oneDrive", default)).Count);
    }

    [TestMethod]
    public async Task ExpiredSessionsAreCleanedWithoutClientPolling()
    {
        var account = await NewOneDrive(); _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        var state = JsonNode.Parse(await File.ReadAllTextAsync(SessionPath(session.Id)))!;
        state["expiresAt"] = DateTimeOffset.UtcNow.AddMinutes(-1);
        await File.WriteAllTextAsync(SessionPath(session.Id), state.ToJsonString());
        await NewAuthorization().CleanupExpiredAsync(default);
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(SessionPath(session.Id))!, "rclone.conf")));
        Assert.AreEqual("expired", (await _authorization.GetAsync(session.Id, default)).Status);
    }

    [TestMethod]
    public void AuthorizationRejectsInjectedNativeOptionsAndCommands()
    {
        var good = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {token = Token})));
        RcloneAuthorizationService.ValidateAuthorizationResult(good);
        RcloneAuthorizationService.ValidateAuthorizationResult(Token);
        var bad = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {token = Token, token_url = "https://attacker.invalid"})));
        Assert.ThrowsException<RcloneRemoteException>(() => RcloneAuthorizationService.ValidateAuthorizationResult(bad));
        Assert.ThrowsException<RcloneRemoteException>(() => RcloneAuthorizationService.AuthorizeCommand("rclone authorize \"onedrive\" && injected"));
        var scopes = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {access_scopes = RcloneAuthorizationService.ReadScopes})));
        Assert.AreEqual($"rclone authorize \"onedrive\" \"{scopes}\"", RcloneAuthorizationService.AuthorizeCommand($"\t rclone authorize \"onedrive\" \"{scopes}\"\n"));
        var injected = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"client_id":"unexpected"}"""));
        Assert.ThrowsException<RcloneRemoteException>(() => RcloneAuthorizationService.AuthorizeCommand($"rclone authorize \"onedrive\" \"{injected}\""));
    }

    [TestMethod]
    public void NativeProcessPassesSecretsOnlyViaOwnedEnvironmentAndUsesNoCallbackListener()
    {
        var proxy = new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(new()));
        var info = RcloneConfigurationProcess.Start("rclone", Path.Combine(_root, "native"), "config/update", new {opt = new {state = "private-state", result = Token}}, proxy);
        Assert.IsFalse(string.Join(' ', info.ArgumentList).Contains("access-canary-private"));
        Assert.IsFalse(info.ArgumentList.Contains("rcd")); Assert.IsFalse(info.ArgumentList.Contains("authorize"));
        Assert.IsTrue(info.ArgumentList.Contains("--continue")); Assert.AreEqual(Token, info.Environment["RCLONE_RESULT"]);
        Assert.AreEqual("private-state", info.Environment["RCLONE_STATE"]); Assert.IsFalse(info.Environment.ContainsKey("RCLONE_JSON"));
        Assert.IsFalse(info.UseShellExecute);
    }

    [TestMethod]
    public void SensitiveAccountAndAuthorizationEndpointsDenyRestrictedUnpairedAccess()
    {
        var access = typeof(RcloneAccountsController).GetCustomAttribute<RemoteAccessibleAttribute>();
        Assert.IsNotNull(access); Assert.IsFalse(access.Allowed);
        foreach (var action in typeof(RcloneAccountsController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            Assert.IsTrue(action.GetCustomAttribute<RemoteAccessibleAttribute>() is not {Allowed: true});
            var scenarios = new (RemoteAccessContext? Remote, bool Allowed)[]
            {
                (null, false),
                (new() {IsLoopback = false, Mode = RemoteAccessMode.Enabled}, false),
                (new() {IsLoopback = false, Mode = RemoteAccessMode.Enabled, Device = new RemoteDevice {Id = "administrator"}}, true),
                (new() {IsLoopback = false, Mode = RemoteAccessMode.Unrestricted}, true),
                (new() {IsLoopback = true, Mode = RemoteAccessMode.Disabled}, true)
            };
            foreach (var (remote, allowed) in scenarios)
            {
                var http = new DefaultHttpContext(); if (remote != null) http.SetRemoteAccessContext(remote);
                var descriptor = new ControllerActionDescriptor {MethodInfo = action, ControllerTypeInfo = typeof(RcloneAccountsController).GetTypeInfo()};
                var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), descriptor), []);
                new RemoteAccessAuthorizationFilter(new ServerSelfDescription(() => ServerKind.Headless)).OnAuthorization(context);
                Assert.AreEqual(allowed, context.Result == null, action.Name);
            }
        }
        Assert.IsFalse(new RcloneAccountInputModel {Name = "account", ProviderId = "pikpak", Password = "password-canary"}.ToString().Contains("password-canary"));
        Assert.IsFalse(new RcloneAuthorizationAnswerInputModel {QuestionId = "id", Value = Token}.ToString().Contains("access-canary-private"));
    }

    [TestMethod]
    public async Task BrowserResultContinuesTheSameNativeQuestionWithoutPublishingAnUnconfirmedAccount()
    {
        var browser = new Browser(); _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive();
        _native.Results.Enqueue(Question("oauth-state", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        var started = await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        Assert.AreEqual("awaitingCallback", started.Browser!.Status);
        _native.Results.Enqueue(Question("drive-state", "config_type", "Select drive", exclusive: true));
        var next = await _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=private&state=fixture"}, default);
        Assert.AreEqual("config_type", next.Question!.Name); Assert.AreEqual("completed", next.Browser!.Status);
        Assert.AreEqual(0, _profiles.OneDriveConfigurations); Assert.AreEqual(1, browser.Submissions);
        Assert.IsTrue(browser.Stops >= 1);
        Assert.IsFalse((await File.ReadAllTextAsync(SessionPath(session.Id))).Contains("access-canary-private"));
    }

    [TestMethod]
    public async Task CancellingOrDeletingAccountStopsItsBrowserAndPreservesOtherAccounts()
    {
        var browser = new Browser(); _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive(); var other = await NewOneDrive();
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        await _authorization.CancelForAccountAsync(other.Id, default);
        Assert.AreEqual("awaitingCallback", (await _authorization.GetAsync(session.Id, default)).Browser!.Status);
        await _authorization.CancelForAccountAsync(account.Id, default);
        Assert.AreEqual("cancelled", (await _authorization.GetAsync(session.Id, default)).Status);
        Assert.IsTrue(browser.Stops > 0);
        Assert.AreEqual(2, (await _accounts.ListAsync("oneDrive", default)).Count);
    }

    [TestMethod]
    public async Task RestartRejectsOldBrowserCallbackButRetainsHeadlessQuestion()
    {
        var browser = new Browser(); _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive(); _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        var stage = Path.Combine(Path.GetDirectoryName(SessionPath(session.Id))!, "browser");
        Directory.CreateDirectory(stage); await File.WriteAllTextAsync(Path.Combine(stage, "rclone.conf"), "stale private canary");
        var restarted = NewAuthorization();
        var status = await restarted.GetAsync(session.Id, default);
        Assert.AreEqual("expired", status.Browser!.Status); Assert.AreEqual("RCLONE_BROWSER_AUTHORIZATION_RESTARTED", status.Browser.Problem);
        Assert.AreEqual(session.Question.Id, status.Question!.Id); Assert.IsFalse(Directory.Exists(stage));
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => restarted.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=private&state=fixture"}, default));
        Assert.AreEqual(0, browser.Submissions);
    }

    [TestMethod]
    public async Task WrongCallbackIsCorrectableButForwardedFailureCannotBlindlyReplay()
    {
        var browser = new Browser(); _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive(); _profiles.Configured.Add(new("oneDrive", account.Id));
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        browser.RejectWithoutForwarding = true;
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "bad"}, default));
        Assert.AreEqual("awaitingCallback", (await _authorization.GetAsync(session.Id, default)).Browser!.Status);
        browser.RejectWithoutForwarding = false; browser.FailAfterForwarding = true;
        var failed = await _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=private&state=fixture"}, default);
        Assert.AreEqual("failed", failed.Browser!.Status);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=private&state=fixture"}, default));
        Assert.AreEqual(1, browser.Submissions); Assert.AreEqual(0, _profiles.OneDriveConfigurations);
        Assert.IsTrue((await _accounts.ListAsync("oneDrive", default)).Single().IsConfigured);
        browser.FailAfterForwarding = false;
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question.Id}, default);
        _native.Results.Enqueue(Question("drive", "config_type", "Select", exclusive: true));
        var restarted = await _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=new-code&state=fixture"}, default);
        Assert.AreEqual("config_type", restarted.Question!.Name); Assert.AreEqual(2, browser.Submissions);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelOrDeleteDuringNativeExchangeDoesNotWaitOnTheSessionGateOrPublishLateCredentials(bool deleteAccount)
    {
        var browser = new Browser {BlockSubmission = true};
        _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive(); var other = await NewOneDrive();
        _profiles.Configured.Add(new("oneDrive", account.Id));
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        var submitted = _authorization.SubmitBrowserCallbackAsync(session.Id,
            new() {QuestionId = session.Question.Id, CallbackUrl = "http://localhost:53682/?code=private&state=fixture"}, default);
        await browser.SubmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (deleteAccount)
        {
            await _authorization.CancelForAccountAsync(account.Id, default).WaitAsync(TimeSpan.FromSeconds(2));
            await _accounts.DeleteAsync(account.Id, default);
        }
        else await _authorization.CancelAsync(session.Id, default).WaitAsync(TimeSpan.FromSeconds(2));
        // Even an incorrectly late successful result from a transport cannot revive a cancelled claim.
        browser.SubmissionResult.TrySetResult(Token);
        var final = await submitted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual("cancelled", final.Status); Assert.AreEqual(0, _profiles.OneDriveConfigurations);
        Assert.AreEqual(1, _native.Calls, "No native config/update may run after cancelling the claim.");
        Assert.IsTrue((await _accounts.ListAsync("oneDrive", default)).Any(a => a.Id == other.Id));
        Assert.AreEqual(deleteAccount ? 1 : 2, (await _accounts.ListAsync("oneDrive", default)).Count);
    }

    [TestMethod]
    public async Task MalformedOrStaleAdvancedAnswerDoesNotStopTheCurrentBrowserFlow()
    {
        var browser = new Browser(); _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, browser);
        var account = await NewOneDrive(); _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\""));
        var session = await _authorization.StartAsync(account.Id, default);
        await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        var stopsBefore = browser.Stops;
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.AnswerAsync(session.Id,
            new() {QuestionId = session.Question.Id, Value = "invalid authorization result"}, default));
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => _authorization.AnswerAsync(session.Id,
            new() {QuestionId = "stale", Value = Token}, default));
        Assert.AreEqual(stopsBefore, browser.Stops);
        Assert.AreEqual("awaitingCallback", (await _authorization.GetAsync(session.Id, default)).Browser!.Status);
    }

    [TestMethod]
    public async Task OfficialNativePreauthorizationCanStartAndStopWithoutMicrosoftLogin()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_RCLONE_TEST_EXECUTABLE");
        if (string.IsNullOrEmpty(executable)) Assert.Inconclusive("Opt-in official binary preauthorization fixture; no Microsoft login.");
        if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(a => a.Port == 53682))
            Assert.Inconclusive("A different owner is using the native fixed callback port.");
        var proxy = new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(new()));
        await using var browser = new RcloneBrowserAuthorizationProcess(_ => Task.FromResult(executable!), proxy);
        var started = await browser.StartAsync("fixture", "question", Path.Combine(_root, "official-native"), DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.AreEqual("awaitingCallback", started.Status, started.Problem);
        Assert.AreEqual("login.microsoftonline.com", new Uri(started.AuthorizationUrl!).Host);
        await Assert.ThrowsExceptionAsync<RcloneRemoteException>(() => browser.SubmitAsync("fixture", "question",
            "http://localhost:53682/?code=not-a-real-code&state=wrong-state", default));
        Assert.AreEqual("awaitingCallback", (await browser.GetAsync("fixture", "question", default))!.Status);
        Assert.AreEqual("cancelled", (await browser.StopAsync("fixture", default))!.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "official-native")));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AutomaticAuthorizationPreservesTheOriginalClientSnapshotAndContinuesNativeDriveSelection(bool readyImmediately)
    {
        var registry = new OAuthProviderRegistry([new MicrosoftGraphOAuthProvider()]);
        var configuration = new OAuthClientConfigurationStore(Path.Combine(_root, "clients"), registry);
        const string client = "11111111-2222-3333-4444-555555555555";
        const string secret = "automatic-client-secret-canary";
        await configuration.SaveAsync("microsoftGraph", new() {Mode = "automatic", ClientId = client,
            ClientSecret = secret, TenantId = "organizations", CallbackUrl = "https://bakabase.example/oauth/callback/microsoftGraph"}, default);
        await using var broker = new OAuthAuthorizationBroker(Path.Combine(_root, "broker"), registry, new OAuthTransport());
        _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, null, configuration, broker);
        var account = await NewOneDrive();
        var privateBlob = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {client_id = client, client_secret = secret})));
        _native.Results.Enqueue(Question("oauth", "config_token", "rclone authorize \"onedrive\" \"" + privateBlob + "\""));
        var session = await _authorization.StartAsync(account.Id, default);
        Assert.AreEqual("automatic", session.AuthorizationMode); Assert.IsNull(session.RemoteAuthorizeCommand);
        Assert.IsFalse(JsonSerializer.Serialize(session).Contains(privateBlob)); Assert.IsFalse(JsonSerializer.Serialize(session).Contains(secret));
        Assert.IsTrue(_native.Inputs[0].Contains(secret));
        // The source selection and private snapshot survive a service reconstruction. A later
        // provider configuration save is deliberately for the NEXT authorization only.
        _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, null, configuration, broker);
        var started = await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        var url = new Uri(started.Browser!.AuthorizationUrl!); var query = QueryHelpers.ParseQuery(url.Query);
        Assert.AreEqual(client, query["client_id"].ToString()); Assert.IsTrue(url.AbsolutePath.Contains("organizations"));
        Assert.IsFalse((await File.ReadAllTextAsync(SessionPath(session.Id))).Contains(query["state"].ToString()));
        await configuration.SaveAsync("microsoftGraph", new() {Mode = "automatic", ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", ClientSecret = "new-secret"}, default);
        if (readyImmediately)
        {
            _native.Results.Enqueue(Json("{\"State\":\"\",\"Error\":\"\"}"));
            _native.Results.Enqueue(Json(JsonSerializer.Serialize(new {type = "onedrive", token = Token, drive_id = "drive", drive_type = "documentLibrary"})));
        }
        else _native.Results.Enqueue(Question("drive", "config_type", "Select drive", exclusive: true));
        Assert.IsTrue(await broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath,
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>() {{"state", query["state"]}, {"code", "fake-code-canary"}}), default));
        var next = await _authorization.GetAsync(session.Id, default);
        if (!readyImmediately)
        {
            Assert.AreEqual("config_type", next.Question!.Name); Assert.AreEqual(0, _profiles.OneDriveConfigurations);
            Assert.AreEqual("completed", next.Browser!.Status);
            _native.Results.Enqueue(Json("{\"State\":\"\",\"Error\":\"\"}"));
            _native.Results.Enqueue(Json(JsonSerializer.Serialize(new {type = "onedrive", token = Token, drive_id = "drive", drive_type = "documentLibrary"})));
            next = await _authorization.AnswerAsync(session.Id, new() {QuestionId = next.Question.Id, Value = "onedrive"}, default);
        }
        Assert.AreEqual("ready", next.Status); Assert.AreEqual(1, _profiles.OneDriveConfigurations);
        Assert.AreEqual(client, _profiles.LastOneDrive!.ClientId); Assert.AreEqual(secret, _profiles.LastOneDrive.ClientSecret);
        Assert.AreEqual("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", _profiles.LastOneDrive.AuthUrl);
        Assert.AreEqual("https://login.microsoftonline.com/organizations/oauth2/v2.0/token", _profiles.LastOneDrive.TokenUrl);
        Assert.IsFalse((await File.ReadAllTextAsync(SessionPath(session.Id))).Contains(secret));
        Assert.AreEqual("completed", (await broker.GetAsync(session.Id, session.Question.Id, default))!.Status,
            "Direct native-ready cleanup must not cancel its own completion callback.");
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellingOrDeletingAnAutomaticExchangePreservesExistingCredentialsAndStopsLateNativeContinuation(bool deleteAccount)
    {
        var registry = new OAuthProviderRegistry([new MicrosoftGraphOAuthProvider()]);
        var configuration = new OAuthClientConfigurationStore(Path.Combine(_root, "clients"), registry);
        await configuration.SaveAsync("microsoftGraph", new() {Mode = "automatic", ClientId = "11111111-2222-3333-4444-555555555555",
            ClientSecret = "private-secret", CallbackUrl = "https://bakabase.example/oauth/callback/microsoftGraph"}, default);
        var transport = new OAuthTransport {Block = true};
        await using var broker = new OAuthAuthorizationBroker(Path.Combine(_root, "broker"), registry, transport);
        _authorization = new(Path.Combine(_root, "authorization"), _accounts, _native, null, configuration, broker);
        var account = await NewOneDrive(); var other = await NewOneDrive();
        _profiles.Configured.Add(new("oneDrive", account.Id));
        _native.Results.Enqueue(Question("oauth", "config_token", "private native help"));
        var session = await _authorization.StartAsync(account.Id, default);
        var started = await _authorization.StartBrowserAsync(session.Id, new() {QuestionId = session.Question!.Id}, default);
        var state = QueryHelpers.ParseQuery(new Uri(started.Browser!.AuthorizationUrl!).Query)["state"];
        var callback = broker.CompleteAsync("microsoftGraph", MicrosoftGraphOAuthProvider.RedirectPath,
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> {{"state", state}, {"code", "not-real-code"}}), default);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (deleteAccount)
        {
            await _authorization.CancelForAccountAsync(account.Id, default).WaitAsync(TimeSpan.FromSeconds(2));
            await _accounts.DeleteAsync(account.Id, default);
        }
        else await _authorization.CancelAsync(session.Id, default).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(await callback.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.AreEqual("cancelled", (await _authorization.GetAsync(session.Id, default)).Status);
        Assert.AreEqual(1, _native.Calls); Assert.AreEqual(0, _profiles.OneDriveConfigurations);
        Assert.IsTrue((await _accounts.ListAsync("oneDrive", default)).Any(value => value.Id == other.Id));
        if (!deleteAccount) Assert.IsTrue((await _accounts.ListAsync("oneDrive", default)).Single(value => value.Id == account.Id).IsConfigured);
    }

    [TestMethod]
    public async Task OfficialNativeCustomApplicationIsUsedAndPersistedWithoutLoginOrSecretArguments()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_RCLONE_TEST_EXECUTABLE");
        if (string.IsNullOrEmpty(executable)) Assert.Inconclusive("Opt-in official binary native configuration fixture; no Microsoft login.");
        var proxy = new BakabaseWebProxy(new TestBOptionsManager<NetworkOptions>(new()));
        var process = new RcloneConfigurationProcess(_ => Task.FromResult(executable!), proxy);
        const string client = "11111111-2222-3333-4444-555555555555";
        const string secret = "official-native-secret-canary";
        var parameters = new Dictionary<string, string> {{"client_id", client}, {"client_secret", secret},
            {"auth_url", "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize"},
            {"token_url", "https://login.microsoftonline.com/organizations/oauth2/v2.0/token"}};
        var input = new {parameters, opt = new {nonInteractive = true}};
        var directory = Path.Combine(_root, "official-custom");
        var start = RcloneConfigurationProcess.Start(executable!, directory, "config/create", input, proxy);
        Assert.IsFalse(string.Join(' ', start.ArgumentList).Contains(secret));
        var native = await process.ExecuteAsync(directory, "config/create", input, default);
        Assert.AreEqual("config_token", native.GetProperty("Option").GetProperty("Name").GetString());
        var help = native.GetProperty("Option").GetProperty("Help").GetString()!;
        var match = System.Text.RegularExpressions.Regex.Match(help, "rclone authorize \"onedrive\" \"([A-Za-z0-9_+/=-]+)\"");
        Assert.IsTrue(match.Success);
        var encoded = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
        using var blob = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
        var dump = await process.ExecuteAsync(directory, "config/get", new {name = "managed"}, default);
        foreach (var parameter in parameters)
        {
            Assert.AreEqual(parameter.Value, blob.RootElement.GetProperty(parameter.Key).GetString(), "Native authorization must use the selected application.");
            Assert.AreEqual(parameter.Value, dump.GetProperty(parameter.Key).GetString(), "Private staging must preserve the same application for continuation/refresh.");
        }
        Assert.IsTrue((await File.ReadAllTextAsync(Path.Combine(directory, "rclone.conf"))).Contains("client_secret = " + secret));
        Assert.IsFalse(help.Contains("http://localhost:53682"), "Noninteractive config must not start a callback listener.");
        var registry = new OAuthProviderRegistry([new MicrosoftGraphOAuthProvider()]);
        var configuration = new OAuthClientConfigurationStore(Path.Combine(_root, "official-clients"), registry);
        await configuration.SaveAsync("microsoftGraph", new() {Mode = "automatic", ClientId = client, ClientSecret = secret,
            TenantId = "organizations", CallbackUrl = "https://bakabase.example/oauth/callback/microsoftGraph"}, default);
        await using var broker = new OAuthAuthorizationBroker(Path.Combine(_root, "official-broker"), registry, new OAuthTransport());
        var service = new RcloneAuthorizationService(Path.Combine(_root, "official-authorization"), _accounts, process, null, configuration, broker);
        var account = await NewOneDrive(); var session = await service.StartAsync(account.Id, default);
        Assert.AreEqual("config_token", session.Question!.Name); Assert.IsNull(session.RemoteAuthorizeCommand);
        Assert.IsFalse(JsonSerializer.Serialize(session).Contains(secret)); Assert.IsFalse(session.Question.Help.Contains(match.Groups[1].Value));
        await service.CancelAsync(session.Id, default);

    }

    private sealed class OAuthTransport : HttpMessageHandler
    {
        public bool Block { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult(); if (Block) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(
                JsonSerializer.Serialize(new {access_token = "automatic-access-canary", refresh_token = "automatic-refresh-canary", token_type = "Bearer", expires_in = 3600}))};
        }
    }

    private Task<RcloneAccountViewModel> NewOneDrive() => _accounts.SaveAsync(new() {Name = "OneDrive", ProviderId = "oneDrive"}, default);
    private string SessionPath(string id) => Path.Combine(_root, "authorization", id, "session.json");
    private static JsonElement Json(string value) { using var parsed = JsonDocument.Parse(value); return parsed.RootElement.Clone(); }
    private static JsonElement Question(string state, string name, string help, bool exclusive = false, string? secretDefault = null) =>
        Json(JsonSerializer.Serialize(new {State = state, Error = "", Option = new {Name = name, Help = help, Type = "string", Required = true,
            IsPassword = false, Exclusive = exclusive, Default = secretDefault, Examples = new[] {new {Value = secretDefault ?? "onedrive", Help = "choice"}}}}));
    private sealed class NativeProcess : IRcloneConfigurationProcess
    {
        public Queue<JsonElement> Results { get; } = new();
        public int Calls { get; private set; }
        public List<string> Inputs { get; } = new();
        public RcloneRemoteException? Failure { get; set; }
        public int FailOnCall { get; set; }
        public RcloneRemoteException? CallFailure { get; set; }
        public Task<JsonElement> ExecuteAsync(string directory, string method, object input, CancellationToken ct)
        {
            Calls++; Inputs.Add(JsonSerializer.Serialize(input)); Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "rclone.conf"), "private staged token canary");
            if (Failure is { } failure) { Failure = null; throw failure; }
            if (Calls == FailOnCall) throw CallFailure!;
            return Task.FromResult(Results.Dequeue());
        }
    }
    private sealed class Browser : IRcloneBrowserAuthorization
    {
        private RcloneBrowserAuthorizationViewModel? _current;
        public int Submissions { get; private set; }
        public int Stops { get; private set; }
        public bool RejectWithoutForwarding { get; set; }
        public bool FailAfterForwarding { get; set; }
        public bool BlockSubmission { get; init; }
        public TaskCompletionSource SubmissionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> SubmissionResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<RcloneBrowserAuthorizationViewModel> StartAsync(string sessionId, string questionId, string directory, DateTimeOffset expiresAt, CancellationToken ct)
        { _current = new() {Status = "awaitingCallback", AuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", ExpiresAt = expiresAt}; return Task.FromResult(_current); }
        public Task<RcloneBrowserAuthorizationViewModel?> GetAsync(string sessionId, string questionId, CancellationToken ct) => Task.FromResult(_current);
        public async Task<string> SubmitAsync(string sessionId, string questionId, string callbackUrl, CancellationToken ct)
        {
            if (RejectWithoutForwarding) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH");
            Submissions++;
            if (BlockSubmission) { _current = _current! with {Status = "exchanging", AuthorizationUrl = null}; SubmissionStarted.TrySetResult(); return await SubmissionResult.Task; }
            if (FailAfterForwarding) { _current = _current! with {Status = "failed", Problem = "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED"}; throw new RcloneRemoteException(_current.Problem); }
            _current = _current! with {Status = "completed", AuthorizationUrl = null}; return Token;
        }
        public Task<RcloneBrowserAuthorizationViewModel?> StopAsync(string sessionId, CancellationToken ct)
        { Stops++; if (_current?.Status is "awaitingCallback" or "exchanging") _current = _current with {Status = "cancelled", AuthorizationUrl = null}; return Task.FromResult(_current); }
    }
    private sealed class Profiles : IRcloneAccountProfiles
    {
        public HashSet<RcloneProfileRef> Configured { get; } = new();
        public int PikPakConfigurations { get; private set; }
        public int OneDriveConfigurations { get; private set; }
        public int Validations { get; private set; }
        public RcloneOneDriveConfiguration? LastOneDrive { get; private set; }
        public Task<bool> HasAsync(RcloneProfileRef profile, CancellationToken ct) => Task.FromResult(Configured.Contains(profile));
        public Task<string?> VersionAsync(RcloneProfileRef profile, CancellationToken ct) => Task.FromResult<string?>(Configured.Contains(profile) ? "generation-1" : null);
        public Task RemoveAsync(RcloneProfileRef profile, CancellationToken ct) { Configured.Remove(profile); return Task.CompletedTask; }
        public Task ConfigurePikPakAsync(string id, string username, string password, CancellationToken ct)
        { PikPakConfigurations++; Configured.Add(new("pikpak", id)); return Task.CompletedTask; }
        public Task ConfigurePixeldrainAsync(string id, string root, string? key, CancellationToken ct)
        { Configured.Add(new("pixeldrain", id)); return Task.CompletedTask; }
        public Task ConfigureOneDriveAsync(string id, RcloneOneDriveConfiguration config, CancellationToken ct)
        { OneDriveConfigurations++; LastOneDrive = config; Configured.Add(new("oneDrive", id)); return Task.CompletedTask; }
        public Task ValidateAsync(RcloneProfileRef profile, CancellationToken ct) { Validations++; return Task.CompletedTask; }
    }
}
