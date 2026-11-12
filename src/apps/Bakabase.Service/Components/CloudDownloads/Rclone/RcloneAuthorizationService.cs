using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Bakabase.Service.Components.OAuth;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>Durable headless native rclone questions. OAuth exchange and validation remain entirely inside rclone.</summary>
public sealed class RcloneAuthorizationService
{
    internal const string ReadScopes = "Files.Read Files.Read.All Sites.Read.All offline_access";
    private readonly string _root;
    private readonly RcloneAccountStore _accounts;
    private readonly IRcloneConfigurationProcess _process;
    private readonly IRcloneBrowserAuthorization? _browser;
    private readonly OAuthClientConfigurationStore? _oauthConfiguration;
    private readonly OAuthAuthorizationBroker? _oauthBroker;
    private readonly IRcloneDriveDiscovery? _drives;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _continuations = new();
    private readonly ConcurrentDictionary<string, byte> _interruptions = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> QuestionNames = new(StringComparer.Ordinal)
    {"config_token", "config_type", "config_driveid", "config_driveid_fixed", "config_siteid", "config_site_url",
        "config_sharepoint_url", "config_search_term", "config_site", "config_drive_ok"};

    public RcloneAuthorizationService(AppService app, RcloneAccountStore accounts, RcloneService dependency, BakabaseWebProxy proxy,
        RcloneBrowserAuthorizationProcess browser, OAuthClientConfigurationStore oauthConfiguration, OAuthAuthorizationBroker oauthBroker,
        RcloneDriveDiscovery drives)
        : this(Path.Combine(app.AppDataDirectory, "cloud-downloads", "rclone-authorization"), accounts, new RcloneConfigurationProcess(dependency, proxy), browser, oauthConfiguration, oauthBroker, drives) { }
    internal RcloneAuthorizationService(string root, RcloneAccountStore accounts, IRcloneConfigurationProcess process, IRcloneBrowserAuthorization? browser = null,
        OAuthClientConfigurationStore? oauthConfiguration = null, OAuthAuthorizationBroker? oauthBroker = null, IRcloneDriveDiscovery? drives = null)
    { _root = Path.GetFullPath(root); _accounts = accounts; _process = process; _browser = browser; _oauthConfiguration = oauthConfiguration; _oauthBroker = oauthBroker; _drives = drives; }

    public async Task<RcloneAuthorizationSessionViewModel> StartAsync(string accountId, CancellationToken ct)
    {
        await _accounts.RequireOneDriveAsync(accountId, ct);
        await _startGate.WaitAsync(ct);
        try
        {
            var client = _oauthConfiguration == null ? null : await _oauthConfiguration.CaptureAsync(MicrosoftGraphOAuthProvider.ProviderId, ct);
            RclonePrivateStorage.Directory(_root);
            await CancelForAccountAsync(accountId, ct);
            var state = new State {Id = Guid.NewGuid().ToString("N"), AccountId = accountId,
                Status = "awaitingInput", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                AuthorizationMode = client == null ? "manual" : "automatic", ClientSnapshot = client};
            RclonePrivateStorage.Directory(DirectoryFor(state.Id));
            await Save(state, ct);
            return await Locked(state.Id, async current =>
            {
                try
                {
                    var result = await _process.ExecuteAsync(DirectoryFor(current.Id), "config/create", new
                    {
                        name = "managed", type = "onedrive", parameters = Parameters(current),
                        opt = new {nonInteractive = true}
                    }, ct);
                    await Update(current, result, ct);
                }
                catch (OperationCanceledException) { current.Status = "cancelled"; await ClearPrivateConfiguration(current); await Save(current, CancellationToken.None); throw; }
                catch (Exception error) when (error is RcloneRemoteException or IOException or JsonException)
                { current.Status = "failed"; current.Problem = "RCLONE_AUTHORIZATION_START_FAILED"; await ClearPrivateConfiguration(current); await Save(current, ct); }
                return View(current);
            }, ct);
        }
        finally { _startGate.Release(); }
    }

    public Task<RcloneAuthorizationSessionViewModel> GetAsync(string id, CancellationToken ct) =>
        Locked(id, state => Task.FromResult(View(state)), ct);

    public Task<RcloneDriveChoicesViewModel> GetDrivesAsync(string id, string questionId, CancellationToken ct) =>
        Locked(id, async state =>
        {
            RequireDriveQuestion(state, questionId);
            if (state.DriveDiscoveryComplete && state.DriveChoices?.QuestionId == questionId) return state.DriveChoices;
            // /me/drive may provision a licensed user's drive. Only repeat it after the
            // native OneDrive branch, which has itself already queried that endpoint.
            if (state.ConnectionType != "onedrive")
            {
                state.DriveChoices ??= UncheckedDrives(state) with {Problem = "RCLONE_DRIVE_PRIMARY_UNVERIFIED"};
                await Save(state, ct); return state.DriveChoices;
            }
            var choices = await QueryDrives(state, async (profile, token) =>
                await _drives!.DescribeAsync(questionId, profile, state.Question!.Examples, token), ct);
            // Transient authentication/network failures are retryable without changing the
            // native question. Only a successfully verified result is cached here.
            if (choices.Problem == null)
            {
                state.DriveChoices = choices with {Candidates = choices.Candidates.Select(c =>
                    c.AccessStatus == "unchecked" && state.DriveChoices?.Candidates.FirstOrDefault(old => old.DriveId == c.DriveId) is {AccessStatus: "accessible"} previous
                        ? c with {AccessStatus = previous.AccessStatus, DriveType = previous.DriveType} : c).ToList()};
                state.DriveDiscoveryComplete = true; await Save(state, ct); return state.DriveChoices;
            }
            return choices;
        }, ct);

    public Task<RcloneDrivePreviewViewModel> PreviewDriveAsync(string id, RcloneDrivePreviewInputModel input, CancellationToken ct) =>
        Locked(id, async state =>
        {
            RequireDriveQuestion(state, input.QuestionId);
            if (string.IsNullOrEmpty(input.DriveId) || input.DriveId.Length > 1024 || input.DriveId.Any(char.IsControl) ||
                !state.Question!.Examples.Any(e => e.Value == input.DriveId))
                throw new RcloneRemoteException("RCLONE_DRIVE_CANDIDATE_INVALID");
            state.DriveChoices ??= UncheckedDrives(state);
            RcloneDrivePreviewViewModel preview;
            try
            {
                preview = await QueryDrives(state, (profile, token) =>
                    _drives!.PreviewAsync(input.QuestionId, input.DriveId, profile, token), ct);
            }
            catch (RcloneRemoteException error)
            {
                state.DriveDiscoveryComplete = false;
                state.DriveChoices = state.DriveChoices with {Candidates = state.DriveChoices.Candidates.Select(c =>
                    c.DriveId == input.DriveId ? c with {IsPrimary = false, AccessStatus = "unavailable", Problem = error.Code} : c).ToList()};
                await Save(state, ct); throw;
            }
            state.DriveChoices = state.DriveChoices with {Candidates = state.DriveChoices.Candidates.Select(c =>
                c.DriveId == input.DriveId ? c with {AccessStatus = "accessible", Problem = null,
                    DriveType = preview.DriveType ?? c.DriveType} : c).ToList()};
            await Save(state, ct);
            return preview;
        }, ct);

    private static RcloneDriveChoicesViewModel UncheckedDrives(State state) => new()
    {
        QuestionId = state.Question!.Id,
        Candidates = RcloneDriveDiscovery.Candidates(state.Question.Examples)
    };

    private static void RequireDriveQuestion(State state, string questionId)
    {
        if (state.Status != "awaitingInput" || state.Question?.Id != questionId ||
            state.Question.Name is not ("config_driveid" or "config_driveid_fixed") || state.RecoveryState != null)
            throw new RcloneRemoteException("RCLONE_AUTHORIZATION_QUESTION_CHANGED");
    }

    private async Task<T> QueryDrives<T>(State state, Func<JsonElement, CancellationToken, Task<T>> query, CancellationToken ct)
    {
        if (_drives == null) throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_UNAVAILABLE");
        await _accounts.RequireOneDriveAsync(state.AccountId, ct);
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = state.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_EXPIRED");
        pending.CancelAfter(remaining < TimeSpan.FromSeconds(20) ? remaining : TimeSpan.FromSeconds(20));
        _continuations[state.Id] = pending;
        if (_interruptions.ContainsKey(state.Id)) pending.Cancel();
        try
        {
            var profile = await _process.ExecuteAsync(DirectoryFor(state.Id), "config/get", new {name = "managed"}, pending.Token);
            var result = await query(profile, pending.Token);
            pending.Token.ThrowIfCancellationRequested(); return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_interruptions.ContainsKey(state.Id))
        { throw new RcloneRemoteException("RCLONE_CONFIGURATION_TIMED_OUT"); }
        catch (Exception error) when (error is IOException or JsonException)
        { throw new RcloneRemoteException("RCLONE_DRIVE_METADATA_INVALID"); }
        finally { _continuations.TryRemove(state.Id, out _); }
    }

    public Task<RcloneAuthorizationSessionViewModel> AnswerAsync(string id, RcloneAuthorizationAnswerInputModel input, CancellationToken ct) =>
        Locked(id, async state =>
        {
            if (state.Status == "ready") return View(state);
            ValidateAnswer(state, input);
            if (state.AuthorizationMode == "automatic" && state.Question?.Name == "config_token")
                throw new OAuthAuthorizationException("OAUTH_AUTOMATIC_AUTHORIZATION_REQUIRED");
            if (state.Question?.Name == "config_token") await StopBrowser(state);
            return await ContinueAnswer(state, input, ct);
        }, ct);

    private async Task<RcloneAuthorizationSessionViewModel> ContinueAnswer(State state, RcloneAuthorizationAnswerInputModel input, CancellationToken ct)
    {
            if (state.Status == "ready") return View(state); // A lost successful response never republishes credentials.
            ValidateAnswer(state, input);
            await _accounts.RequireOneDriveAsync(state.AccountId, ct);
            using var continuation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = state.ExpiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_EXPIRED");
            continuation.CancelAfter(remaining); _continuations[state.Id] = continuation;
            if (_interruptions.ContainsKey(state.Id)) continuation.Cancel();
            try
            {
                if (state.Question?.Name == "config_type" && state.RecoveryState == null) state.ConnectionType = input.Value;
                var result = await _process.ExecuteAsync(DirectoryFor(state.Id), "config/update", new
                {
                    name = "managed", parameters = Parameters(state),
                    opt = new {nonInteractive = true, @continue = true, state = state.RecoveryState ?? state.NativeState,
                        result = state.RecoveryState == null ? input.Value : state.RecoveryResult ?? ""}
                }, continuation.Token);
                continuation.Token.ThrowIfCancellationRequested();
                await Update(state, result, continuation.Token, state.RecoveryState == null ? null : state.RecoveryProblem);
            }
            catch (RcloneRemoteException error) { state.Problem = error.Code; await Save(state, ct); }
            finally { _continuations.TryRemove(state.Id, out _); }
            return View(state);
    }

    private static void ValidateAnswer(State state, RcloneAuthorizationAnswerInputModel input)
    {
            if (state.Status != "awaitingInput" || state.Question == null || state.Question.Id != input.QuestionId)
                throw new RcloneRemoteException("RCLONE_AUTHORIZATION_QUESTION_CHANGED");
            var question = state.Question;
            if (input.Value == null || input.Value.Length > 256 * 1024 || input.Value.Contains('\0') ||
                question.Required && string.IsNullOrWhiteSpace(input.Value) ||
                question.Name is "config_driveid" or "config_driveid_fixed" && string.IsNullOrWhiteSpace(input.Value) ||
                question.Exclusive && !question.Examples.Any(e => e.Value == input.Value))
                throw new RcloneRemoteException("RCLONE_CONFIGURATION_ANSWER_INVALID");
            if (question.Name == "config_token") ValidateAuthorizationResult(input.Value);
    }

    public Task<RcloneAuthorizationSessionViewModel> StartBrowserAsync(string id, RcloneBrowserAuthorizationStartInputModel input, CancellationToken ct) =>
        Locked(id, async state =>
        {
            RequireBrowserQuestion(state, input.QuestionId);
            if (state.AuthorizationMode == "automatic")
            {
                if (_oauthBroker == null || state.ClientSnapshot == null) throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_NOT_READY");
                state.BrowserQuestionId = input.QuestionId;
                state.Browser = BrowserView(await _oauthBroker.StartAsync(id, input.QuestionId, state.ClientSnapshot, state.ExpiresAt,
                    (grant, token) => CompleteAutomaticAsync(id, input.QuestionId, grant, token), ct));
                await Save(state, CancellationToken.None); return View(state);
            }
            if (_browser == null) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_NOT_RUNNING");
            // The native question must itself contain only our reviewed public scopes/default application.
            AuthorizeCommand(state.Question!.Help);
            state.BrowserQuestionId = input.QuestionId;
            var replacingTerminalFlow = state.Browser?.Status is not ("awaitingCallback" or "exchanging");
            try { state.Browser = await _browser.StartAsync(id, input.QuestionId, Path.Combine(DirectoryFor(id), "browser"), state.ExpiresAt, ct); }
            catch (RcloneRemoteException error) { state.Browser = new() {Status = "failed", ExpiresAt = state.ExpiresAt, Problem = error.Code}; }
            catch (OperationCanceledException)
            { state.Browser = await _browser.GetAsync(id, input.QuestionId, CancellationToken.None); await Save(state, CancellationToken.None); throw; }
            if (replacingTerminalFlow && state.Browser?.Status == "awaitingCallback") state.BrowserSubmissionId = null;
            await Save(state, CancellationToken.None); return View(state);
        }, ct);

    public async Task<RcloneAuthorizationSessionViewModel> SubmitBrowserCallbackAsync(string id, RcloneBrowserAuthorizationCallbackInputModel input, CancellationToken ct)
    {
        var claim = Guid.NewGuid().ToString("N");
        await Locked(id, async state =>
        {
            RequireBrowserQuestion(state, input.QuestionId);
            if (state.AuthorizationMode != "manual") throw new OAuthAuthorizationException("OAUTH_AUTOMATIC_AUTHORIZATION_REQUIRED");
            if (state.BrowserSubmissionId != null) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_ALREADY_SUBMITTED");
            if (_browser == null || state.Browser?.Status != "awaitingCallback")
                throw new RcloneRemoteException(state.Browser?.Problem ?? "RCLONE_BROWSER_AUTHORIZATION_NOT_RUNNING");
            state.BrowserSubmissionId = claim;
            state.Browser = state.Browser with {Status = "exchanging", AuthorizationUrl = null};
            await Save(state, ct); return true;
        }, ct);
        string nativeResult;
        try { nativeResult = await _browser!.SubmitAsync(id, input.QuestionId, input.CallbackUrl, ct); }
        catch (Exception error) when (error is RcloneRemoteException or OperationCanceledException)
        {
            var correctable = error is OperationCanceledException || error is RcloneRemoteException {Code:
                "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_INVALID" or "RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH" or "RCLONE_BROWSER_AUTHORIZATION_QUESTION_CHANGED"};
            var status = await Locked(id, async state =>
            {
                if (state.Status == "awaitingInput" && state.Question?.Id == input.QuestionId && state.BrowserSubmissionId == claim)
                {
                    state.Browser = await _browser!.GetAsync(id, input.QuestionId, CancellationToken.None) ??
                        new() {Status = "failed", ExpiresAt = state.ExpiresAt, Problem = error is RcloneRemoteException known ? known.Code : "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED"};
                    if (correctable && state.Browser.Status == "awaitingCallback") state.BrowserSubmissionId = null;
                    await Save(state, CancellationToken.None);
                }
                return View(state);
            }, CancellationToken.None);
            if (correctable) throw;
            return status;
        }
        // Do not hold the session gate while waiting for the browser/transport. Cancel/delete
        // can terminate the same native process immediately; they invalidate this claim.
        return await Locked(id, async state =>
        {
            if (state.Status != "awaitingInput" || state.Question?.Id != input.QuestionId || state.BrowserSubmissionId != claim)
                return View(state);
            state.Browser = await _browser!.GetAsync(id, input.QuestionId, CancellationToken.None);
            await StopBrowser(state);
            return await ContinueAnswer(state, new() {QuestionId = input.QuestionId, Value = nativeResult}, CancellationToken.None);
        }, CancellationToken.None);
    }

    public async Task<RcloneAuthorizationSessionViewModel> StopBrowserAsync(string id, CancellationToken ct)
    {
        var stopped = await Interrupt(id, ct);
        try { return await Locked(id, async state =>
        {
            if (stopped != null) state.Browser = stopped;
            state.BrowserSubmissionId = null;
            await StopBrowser(state); await Save(state, ct); return View(state);
        }, ct); }
        finally { _interruptions.TryRemove(id, out _); }
    }

    private async Task<RcloneBrowserAuthorizationViewModel?> Interrupt(string id, CancellationToken ct)
    {
        _interruptions[id] = 0;
        if (_continuations.TryGetValue(id, out var continuation))
        {
            try { continuation.Cancel(); } catch (ObjectDisposedException) { }
        }
        var automatic = _oauthBroker == null ? null : await _oauthBroker.StopAsync(id, ct);
        var manual = _browser == null ? null : await _browser.StopAsync(id, ct);
        return automatic == null ? manual : BrowserView(automatic);
    }

    public async Task CancelForAccountAsync(string accountId, CancellationToken ct)
    {
        RcloneRemoteEngine.RejectLinks(_root);
        if (!Directory.Exists(_root)) return;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            var path = Path.Combine(directory, "session.json"); RcloneRemoteEngine.RejectLinks(path);
            if (!File.Exists(path)) continue;
            var candidate = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct), Json);
            if (candidate?.AccountId != accountId) continue;
            await CancelAsync(id, ct);
        }
    }

    private static void RequireBrowserQuestion(State state, string questionId)
    {
        if (state.Status != "awaitingInput" || state.Question?.Name != "config_token" || state.Question.Id != questionId)
            throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_QUESTION_CHANGED");
    }

    private async Task StopBrowser(State state)
    {
        // A successful automatic native continuation can reach ready synchronously inside the
        // broker completion callback. Do not cancel that callback's own lifetime during cleanup.
        if (state.AuthorizationMode == "automatic" && state.Status != "ready" && _oauthBroker != null)
            state.Browser = BrowserView(await _oauthBroker.StopAsync(state.Id, CancellationToken.None)) ?? state.Browser;
        if (_browser != null) state.Browser = await _browser.StopAsync(state.Id, CancellationToken.None) ?? state.Browser;
    }

    private async Task CompleteAutomaticAsync(string id, string questionId, OAuthAuthorizationGrant grant, CancellationToken ct)
    {
        await Locked(id, async state =>
        {
            ct.ThrowIfCancellationRequested();
            RequireBrowserQuestion(state, questionId);
            if (state.AuthorizationMode != "automatic" || state.BrowserQuestionId != questionId || state.ClientSnapshot == null ||
                state.ClientSnapshot.ConfigurationVersion != grant.Client.ConfigurationVersion || state.ClientSnapshot.ClientId != grant.Client.ClientId ||
                _interruptions.ContainsKey(id)) throw new OAuthAuthorizationException("OAUTH_AUTHORIZATION_CANCELLED");
            var result = await ContinueAnswer(state, new() {QuestionId = questionId, Value = grant.TokenJson}, ct);
            if (result.Status != "ready" && result.Question?.Id == questionId)
                throw new OAuthAuthorizationException("OAUTH_NATIVE_CONFIGURATION_FAILED");
            return true;
        }, ct);
    }

    private static Dictionary<string, string> Parameters(State state)
    {
        var values = new Dictionary<string, string> {{"config_is_local", "false"}, {"access_scopes", ReadScopes}};
        if (state.ClientSnapshot is { } client)
        {
            values["client_id"] = client.ClientId; values["client_secret"] = client.ClientSecret;
            values["auth_url"] = client.AuthEndpoint; values["token_url"] = client.TokenEndpoint;
        }
        return values;
    }
    private static RcloneBrowserAuthorizationViewModel? BrowserView(OAuthAuthorizationView? value) => value == null ? null : new()
        {Status = value.Status, AuthorizationUrl = value.AuthorizationUrl, ExpiresAt = value.ExpiresAt, Problem = value.Problem};

    public async Task CancelAsync(string id, CancellationToken ct)
    {
        var stopped = await Interrupt(id, ct);
        try { await Locked(id, async state =>
        {
            if (state.Status != "ready")
            {
                if (stopped != null) state.Browser = stopped;
                state.BrowserSubmissionId = null; state.Status = "cancelled";
                await ClearPrivateConfiguration(state); await Save(state, ct);
            }
            return true;
        }, ct); }
        finally { _interruptions.TryRemove(id, out _); }
    }

    internal async Task CleanupExpiredAsync(CancellationToken ct)
    {
        RcloneRemoteEngine.RejectLinks(_root);
        if (!Directory.Exists(_root)) return;
        foreach (var directory in Directory.EnumerateDirectories(_root))
        {
            ct.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try { await Locked(id, _ => Task.FromResult(true), ct); }
            catch (Exception error) when (error is RcloneRemoteException or IOException or JsonException)
            { /* Never inspect another directory or copy malformed state into a public error. */ }
        }
    }

    private async Task Update(State state, JsonElement result, CancellationToken ct, string? recoveryProblem = null)
    {
        var visited = new HashSet<(string State, string Result)>();
        for (var attempt = 0; ; attempt++)
        {
            if (result.ValueKind != JsonValueKind.Object) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
            var nativeState = Text(result, "State") ?? "";
            var nativeError = Text(result, "Error");
            if (!string.IsNullOrEmpty(nativeError)) recoveryProblem = RcloneConfigurationErrors.Classify(nativeError);
            if (nativeState.Length > 16384) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
            if (nativeState.Length == 0)
            {
                if (recoveryProblem != null) throw new RcloneRemoteException(recoveryProblem);
                var profile = await _process.ExecuteAsync(DirectoryFor(state.Id), "config/get", new {name = "managed"}, ct);
                if (Text(profile, "type") != "onedrive" || Text(profile, "drive_type") is not ("personal" or "business" or "documentLibrary"))
                    throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
                var token = Text(profile, "token") ?? ""; ValidateToken(token);
                await _accounts.ApplyOneDriveAsync(state.AccountId, new()
                {
                    TokenJson = token, DriveId = Text(profile, "drive_id") ?? "", DriveType = Text(profile, "drive_type")!,
                    RootFolderId = Text(profile, "root_folder_id"), ClientId = state.ClientSnapshot?.ClientId,
                    ClientSecret = state.ClientSnapshot?.ClientSecret, AuthUrl = state.ClientSnapshot?.AuthEndpoint, TokenUrl = state.ClientSnapshot?.TokenEndpoint
                }, ct);
                state.Status = "ready"; state.Problem = null; await ClearPrivateConfiguration(state);
                await Save(state, CancellationToken.None); return;
            }
            if (!result.TryGetProperty("Option", out var option) || option.ValueKind != JsonValueKind.Object)
            {
                if (result.TryGetProperty("Result", out var returnedResult) && returnedResult.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
                var nextResult = Text(result, "Result") ?? "";
                if (nextResult.Length > 256 * 1024 || nextResult.Contains('\0'))
                    throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
                // ConfigError and ConfigResult are native transitions. Persist their exact
                // state/result before advancing, so retry never replays the old user answer.
                // The returned result stays private; it is never an API question/default.
                state.RecoveryState = nativeState; state.RecoveryResult = nextResult;
                state.RecoveryProblem = recoveryProblem; state.Problem = recoveryProblem;
                await Save(state, ct);
                if (attempt >= 4 || !visited.Add((nativeState, nextResult)))
                {
                    state.Problem = "RCLONE_CONFIGURATION_RECOVERY_LIMIT";
                    await Save(state, ct); return;
                }
                result = await _process.ExecuteAsync(DirectoryFor(state.Id), "config/update", new
                {
                    name = "managed", parameters = Parameters(state),
                    opt = new {nonInteractive = true, @continue = true, state = nativeState, result = nextResult}
                }, ct);
                ct.ThrowIfCancellationRequested();
                continue;
            }
            var name = Text(option, "Name") ?? "";
            if (!QuestionNames.Contains(name)) throw new RcloneRemoteException("RCLONE_CONFIGURATION_QUESTION_UNSUPPORTED");
            var help = Text(option, "Help") ?? "";
            if (help.Length > 32768) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
            var secret = name == "config_token" || Bool(option, "IsPassword");
            if (name == "config_token" && state.AuthorizationMode == "automatic")
                help = "Use the configured Microsoft application to sign in. Continue in Bakabase after authorization.";
            var examples = new List<RcloneConfigurationExampleViewModel>();
            if (!secret && option.TryGetProperty("Examples", out var values) && values.ValueKind == JsonValueKind.Array)
                foreach (var example in values.EnumerateArray())
                {
                    if (examples.Count >= 10000) throw new RcloneRemoteException("RCLONE_CONFIGURATION_OUTPUT_INVALID");
                    examples.Add(new(Text(example, "Value") ?? "", Text(example, "Help") ?? ""));
                }
            state.RecoveryState = null; state.RecoveryResult = null; state.RecoveryProblem = null; state.DriveChoices = null; state.DriveDiscoveryComplete = false;
            state.NativeState = nativeState;
            state.Question = new() {Id = Guid.NewGuid().ToString("N"), Name = name, Help = help,
                Type = Text(option, "Type") ?? "string", Required = Bool(option, "Required"), IsSecret = secret,
                Exclusive = Bool(option, "Exclusive"), DefaultValue = secret ? null : Value(option, "Default"), Examples = examples};
            state.RemoteAuthorizeCommand = name == "config_token" && state.AuthorizationMode == "manual" ? AuthorizeCommand(help) : null;
            state.Problem = recoveryProblem;
            await Save(state, ct); return;
        }
    }

    internal static string AuthorizeCommand(string help)
    {
        try
        {
        var match = Regex.Match(help, "^\\s*(rclone authorize \\\"onedrive\\\"(?: \\\"([A-Za-z0-9_+/=-]+)\\\")?)\\s*$", RegexOptions.Multiline);
        if (!match.Success) throw new RcloneRemoteException("RCLONE_AUTHORIZATION_COMMAND_INVALID");
        if (match.Groups[2].Success)
        {
            using var blob = JsonDocument.Parse(Decode(match.Groups[2].Value));
            foreach (var field in blob.RootElement.EnumerateObject())
                if (field.Name != "access_scopes" || field.Value.GetString() != ReadScopes)
                    throw new RcloneRemoteException("RCLONE_AUTHORIZATION_COMMAND_INVALID");
        }
        return match.Groups[1].Value.Trim();
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        { throw new RcloneRemoteException("RCLONE_AUTHORIZATION_COMMAND_INVALID"); }
    }
    internal static void ValidateAuthorizationResult(string value)
    {
        try
        {
            if (value.TrimStart().StartsWith('{')) { ValidateToken(value); return; }
            using var blob = JsonDocument.Parse(Decode(value.Trim()));
            var properties = blob.RootElement.EnumerateObject().ToList();
            if (properties.Count != 1 || properties[0].Name != "token" || properties[0].Value.ValueKind != JsonValueKind.String)
                throw new RcloneRemoteException("RCLONE_AUTHORIZATION_RESULT_INVALID");
            ValidateToken(properties[0].Value.GetString()!);
        }
        catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
        { throw new RcloneRemoteException("RCLONE_AUTHORIZATION_RESULT_INVALID"); }
    }
    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
    }
    private static void ValidateToken(string value)
    {
        try
        {
        using var token = JsonDocument.Parse(value);
        if (token.RootElement.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(Text(token.RootElement, "access_token")) ||
            string.IsNullOrWhiteSpace(Text(token.RootElement, "refresh_token")))
            throw new RcloneRemoteException("RCLONE_AUTHORIZATION_RESULT_INVALID");
        }
        catch (JsonException) { throw new RcloneRemoteException("RCLONE_AUTHORIZATION_RESULT_INVALID"); }
    }
    private async Task<T> Locked<T>(string id, Func<State, Task<T>> action, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new RcloneRemoteException("RCLONE_AUTHORIZATION_INVALID");
        var gate = _gates.GetOrAdd(id, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var path = Path.Combine(DirectoryFor(id), "session.json"); RcloneRemoteEngine.RejectLinks(path);
            if (!File.Exists(path)) throw new RcloneRemoteException("RCLONE_AUTHORIZATION_NOT_FOUND");
            var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct), Json)
                ?? throw new RcloneRemoteException("RCLONE_AUTHORIZATION_INVALID");
            if (state.Id != id) throw new RcloneRemoteException("RCLONE_AUTHORIZATION_INVALID");
            if (state.ExpiresAt <= DateTimeOffset.UtcNow && state.Status == "awaitingInput")
            { state.Status = "expired"; await ClearPrivateConfiguration(state); await Save(state, ct); }
            if (state.Browser?.Status is "awaitingCallback" or "exchanging")
            {
                var current = state.AuthorizationMode == "automatic"
                    ? BrowserView(_oauthBroker == null ? null : await _oauthBroker.GetAsync(id, state.BrowserQuestionId ?? "", ct))
                    : _browser == null ? null : await _browser.GetAsync(id, state.BrowserQuestionId ?? "", ct);
                state.Browser = current ?? new() {Status = "expired", ExpiresAt = state.Browser.ExpiresAt,
                    Problem = "RCLONE_BROWSER_AUTHORIZATION_RESTARTED"};
                if (current == null) RcloneBrowserAuthorizationProcess.ClearStage(Path.Combine(DirectoryFor(id), "browser"));
                await Save(state, ct);
            }
            return await action(state);
        }
        finally { gate.Release(); }
    }
    private string DirectoryFor(string id) => Path.Combine(_root, id);
    private Task Save(State state, CancellationToken ct)
    {
        var saved = JsonSerializer.SerializeToNode(state, Json)!;
        // Raw automatic OAuth state lives only in the active broker. Restart keeps enough
        // metadata to reject the old flow, and a live read reconstructs its URL from the broker.
        if (state.AuthorizationMode == "automatic" && saved["browser"] is { } browser)
            browser["authorizationUrl"] = null;
        return RclonePrivateStorage.WriteAsync(Path.Combine(DirectoryFor(state.Id), "session.json"), saved.ToJsonString(), ct);
    }
    private async Task ClearPrivateConfiguration(State state)
    {
        await StopBrowser(state);
        state.ClientSnapshot = null;
        state.NativeState = ""; state.RecoveryState = null; state.RecoveryResult = null; state.RecoveryProblem = null;
        state.Question = null; state.RemoteAuthorizeCommand = null;
        state.DriveChoices = null; state.DriveDiscoveryComplete = false;
        var directory = DirectoryFor(state.Id); RcloneRemoteEngine.RejectLinks(directory);
        foreach (var file in Directory.EnumerateFiles(directory).Where(p => Path.GetFileName(p) != "session.json"))
        { RcloneRemoteEngine.RejectLinks(file); File.Delete(file); }
        foreach (var child in Directory.EnumerateDirectories(directory))
        { RcloneRemoteEngine.RejectLinks(child); Directory.Delete(child, true); }
    }
    private static RcloneAuthorizationSessionViewModel View(State state) => new()
        {Id = state.Id, AccountId = state.AccountId, Status = state.Status, ExpiresAt = state.ExpiresAt,
            Question = state.Question, RemoteAuthorizeCommand = state.RemoteAuthorizeCommand, Problem = state.Problem, Browser = state.Browser,
            AuthorizationMode = state.AuthorizationMode};
    private static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static string? Value(JsonElement value, string key) => !value.TryGetProperty(key, out var field) ? null : field.ValueKind switch
        {JsonValueKind.String => field.GetString(), JsonValueKind.True => "true", JsonValueKind.False => "false",
            JsonValueKind.Number => field.GetRawText(), _ => null};
    private static bool Bool(JsonElement value, string key) => value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.True;
    private sealed class State
    {
        public State() { }
        public string Id { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public string NativeState { get; set; } = "";
        public string? RecoveryState { get; set; }
        public string? RecoveryResult { get; set; }
        public string? RecoveryProblem { get; set; }
        public RcloneConfigurationQuestionViewModel? Question { get; set; }
        public string? RemoteAuthorizeCommand { get; set; }
        public string? Problem { get; set; }
        public string AuthorizationMode { get; set; } = "manual";
        public OAuthClientSnapshot? ClientSnapshot { get; set; }
        public string? BrowserQuestionId { get; set; }
        public string? BrowserSubmissionId { get; set; }
        public RcloneBrowserAuthorizationViewModel? Browser { get; set; }
        public RcloneDriveChoicesViewModel? DriveChoices { get; set; }
        public bool DriveDiscoveryComplete { get; set; }
        public string? ConnectionType { get; set; }
    }
}
