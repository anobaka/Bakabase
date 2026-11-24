using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.PikPakCli;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

/// <summary>One managed official-CLI connection. Refreshable credentials stay in private CLI
/// profiles; only opaque generations and bounded device-challenge views leave this service.</summary>
public sealed class PikPakCliAccountService
{
    private const string ManageFilesScope = "files:manage";
    private readonly string _root;
    private readonly IPikPakCliProcess _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transfers = new(StringComparer.Ordinal);
    public PikPakCliAccountService(AppService app, PikPakCliService dependency, BakabaseWebProxy proxy)
        : this(Path.Combine(app.AppDataDirectory, "cloud-downloads", "pikpak-cli"), new PikPakCliProcess(dependency, proxy)) { }
    internal PikPakCliAccountService(string root, IPikPakCliProcess process) { _root = Path.GetFullPath(root); _process = process; }
    internal string Root => _root;
    public Task<PikPakCliAccountViewModel> ResolveConfiguredAccountAsync(string? accountId, CancellationToken ct) =>
        WithAccountAsync(accountId, null, account => Task.FromResult(new PikPakCliAccountViewModel
        {
            IsConfigured = true, AccountId = account.Id, AccountVersion = account.Version,
            CanManageFiles = account.CanManageFiles, TrashTemporaryCopiesAfterDownload = account.TrashTemporaryCopiesAfterDownload
        }), ct);
    private string AccountPath => Path.Combine(_root, "account.json");
    private string ActiveAuthorizationPath => Path.Combine(_root, "active-authorization.json");
    private string Profile(string id) { CheckId(id); return Path.Combine(_root, "profiles", id); }
    private string SessionPath(string id) { CheckId(id); return Path.Combine(_root, "sessions", id + ".json"); }
    private static void CheckId(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new PikPakCliException("PIKPAK_ACCOUNT_INVALID"); }

    public async Task<PikPakCliAccountViewModel> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var account = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct);
            if (account == null) return new() {Problem = "PIKPAK_ACCOUNT_REQUIRED"};
            // Configuration views must remain responsive while a large CLI download holds
            // its own profile's command lock. Live permission checks happen in provider work.
            return View(account);
        }
        finally { _gate.Release(); }
    }

    public async Task<PikPakCliAccountViewModel> SetCleanupSettingsAsync(
        PikPakCliCleanupSettingsInputModel input, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var account = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct)
                ?? throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
            if (account.Id != input.ExpectedAccountId || account.Version != input.ExpectedAccountVersion)
                throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
            if (input.TrashTemporaryCopiesAfterDownload && !account.CanManageFiles)
                throw new PikPakCliException("PIKPAK_MANAGE_FILES_SCOPE_REQUIRED");
            if (input.TrashTemporaryCopiesAfterDownload == account.TrashTemporaryCopiesAfterDownload)
                return View(account);
            if (_leases.GetValueOrDefault(account.Id) != 0) throw new PikPakCliException("PIKPAK_ACCOUNT_IN_USE");
            account = account with {TrashTemporaryCopiesAfterDownload = input.TrashTemporaryCopiesAfterDownload};
            await PikPakCliStorage.WriteAsync(AccountPath, account, ct);
            return View(account);
        }
        finally { _gate.Release(); }
    }

    public async Task<PikPakCliAuthorizationViewModel?> GetActiveAuthorizationAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var id = await PikPakCliStorage.ReadAsync<string>(ActiveAuthorizationPath, ct);
            if (id == null) return null;
            var session = await ReadSession(id, ct);
            // The page may have been closed after the CLI exchanged the device code, but
            // before account promotion. Resume only this server-owned active session.
            if (CanRecoverCredentials(session) && HasProfileCredentials(id))
                return View(session) with {Status = "pending", Problem = null};
            return session.Status == "pending" && session.ExpiresAt <= DateTimeOffset.UtcNow
                ? View(session) with {Status = "expired", VerificationUri = null, UserCode = null}
                : View(session);
        }
        finally { _gate.Release(); }
    }

    public Task<PikPakCliAuthorizationViewModel> StartAsync(CancellationToken ct) => StartAsync(false, ct);

    public Task<PikPakCliAuthorizationViewModel> StartManageFilesAuthorizationAsync(CancellationToken ct) => StartAsync(true, ct);

    private async Task<PikPakCliAuthorizationViewModel> StartAsync(bool manageFiles, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_leases.Values.Any(n => n > 0)) throw new PikPakCliException("PIKPAK_ACCOUNT_IN_USE");
            var current = manageFiles ? await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct) : null;
            var expectedIdentity = current?.ProviderIdentity;
            if (current != null && expectedIdentity == null)
            {
                // Older registrations did not save an identity. Check the original profile
                // before opening the challenge, while the account cannot be replaced.
                var status = await _process.RunAsync(Profile(current.EffectiveProfileId), ["auth", "status"], ct);
                if (!PikPakCliJson.True(status, "logged_in"))
                    throw new PikPakCliException("PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED");
                expectedIdentity = ReadProviderIdentity(status)
                    ?? throw new PikPakCliException("PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED");
            }
            PikPakCliStorage.Directory(_root); PikPakCliStorage.Directory(Path.Combine(_root, "profiles"));
            var id = Guid.NewGuid().ToString("N");
            // Account scope permits the CLI's post-login identity lookup. File management
            // includes read/write and moving items to the trash. Never request purge.
            var scopes = manageFiles ? $"account,{ManageFilesScope}" : "account,files:write";
            var result = await _process.RunAsync(Profile(id), ["auth", "login", "--method", "device", "--no-launch-browser",
                "--scope", scopes, "--label", "Bakabase"], ct);
            var verification = PikPakCliJson.Required(result, "verification_uri");
            if (!Uri.TryCreate(verification, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.IdnHost is not ("mypikpak.com" or "www.mypikpak.com" or "user.mypikpak.com"))
                throw new PikPakCliException("PIKPAK_AUTHORIZATION_RESPONSE_INVALID");
            var session = new Session {Id = id, DeviceCode = PikPakCliJson.Required(result, "device_code"),
                VerificationUri = verification, UserCode = PikPakCliJson.Required(result, "user_code"),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(PikPakCliJson.Number(result, "expires_in") ?? 300, 30, 1800)),
                IntervalSeconds = (int)Math.Clamp(PikPakCliJson.Number(result, "interval") ?? 3, 2, 30),
                ManageFilesRequested = manageFiles, PreviousAccountId = current?.Id, PreviousAccountVersion = current?.Version,
                PreviousProviderIdentity = expectedIdentity};
            await CancelActivePending(ct);
            await PikPakCliStorage.WriteAsync(SessionPath(id), session, ct);
            await PikPakCliStorage.WriteAsync(ActiveAuthorizationPath, id, ct);
            return View(session);
        }
        finally { _gate.Release(); }
    }

    public async Task<PikPakCliAuthorizationViewModel> PollAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = await ReadSession(id, ct);
            if (await PikPakCliStorage.ReadAsync<string>(ActiveAuthorizationPath, ct) != id)
                return View(session) with {Status = "cancelled", VerificationUri = null, UserCode = null};
            if (session.Status != "pending" && !(CanRecoverCredentials(session) && HasProfileCredentials(id)))
                return View(session);
            if (_leases.Values.Any(n => n > 0)) return View(session) with {Problem = "PIKPAK_ACCOUNT_IN_USE"};
            if (session.NextPollAt <= DateTimeOffset.UtcNow)
            {
                session.NextPollAt = DateTimeOffset.UtcNow.AddSeconds(session.IntervalSeconds);
                await PikPakCliStorage.WriteAsync(SessionPath(id), session, ct);
                try
                {
                    // A previous process may have committed credentials before its response
                    // was interrupted. Do not exchange the same device code a second time.
                    if ((HasProfileCredentials(id) || session.ManageFilesConsentVerified) &&
                        await TryPromote(session, ct)) return View(session);
                    if (session.Status != "pending") return View(session);
                    if (session.ExpiresAt <= DateTimeOffset.UtcNow)
                    { session.Status = "expired"; session.DeviceCode = ""; }
                    else if (session.ManageFilesConsentVerified)
                        session.Problem = "PIKPAK_AUTHORIZATION_AWAITING_CREDENTIALS";
                    else
                    {
                        var result = await _process.RunAsync(Profile(id), ["auth", "login", "--continue", session.DeviceCode], ct);
                        session.Problem = null;
                        var status = PikPakCliJson.Text(result, "status")?.ToLowerInvariant();
                        session.LastProviderStatus = status is "success" or "pending" or "slow_down" or "denied" or "expired" or "error"
                            ? status : "unknown";
                        var explicitScopes = ReadGrantedScopes(result, out var knownScopeList);
                        if (explicitScopes != null) session.GrantedScopes = explicitScopes;
                        if (session.ManageFilesRequested && status == "success")
                        {
                            if (explicitScopes?.Contains("account") != true || !explicitScopes.Contains(ManageFilesScope) ||
                                explicitScopes.Contains("files:purge"))
                            {
                                await RejectPromotionAsync(session, "PIKPAK_MANAGE_FILES_SCOPE_REQUIRED");
                                return View(session);
                            }
                            // The CLI may already have written credentials. Save the scope
                            // evidence before checking identity so promotion can resume.
                            session.ManageFilesConsentVerified = true;
                            await PikPakCliStorage.WriteAsync(SessionPath(id), session, CancellationToken.None);
                        }
                        switch (status)
                        {
                            case "pending": case "authorization_pending": break;
                            case "slow_down": SlowDown(session); break;
                            case "expired": case "expired_token":
                                session.Status = "expired"; session.DeviceCode = ""; break;
                            case "denied": case "access_denied": case "cancelled":
                                session.Status = "failed"; session.DeviceCode = "";
                                session.Problem = "PIKPAK_AUTHORIZATION_DENIED"; break;
                            default:
                                // No status, unknown status, and a reported success are not proof
                                // of usable credentials. Keep the challenge on uncertain output.
                                if (!await TryPromote(session, ct))
                                {
                                    if (status == "success" && knownScopeList && explicitScopes != null && !explicitScopes.Contains("account"))
                                    {
                                        // Exact official success output names the granted scopes.
                                        // Do not repeat consent or rescue a token the CLI rejected.
                                        session.Status = "failed"; session.DeviceCode = "";
                                        session.Problem = "PIKPAK_ACCOUNT_SCOPE_REQUIRED";
                                    }
                                    else session.Problem = "PIKPAK_AUTHORIZATION_AWAITING_CREDENTIALS";
                                }
                                break;
                        }
                    }
                }
                catch (PikPakCliException error)
                {
                    session.Problem = error.Code;
                    session.LastProviderStatus = error.Code switch
                    {
                        "PIKPAK_AUTHORIZATION_PENDING" => "pending", "PIKPAK_AUTHORIZATION_SLOW_DOWN" => "slow_down",
                        "PIKPAK_AUTHORIZATION_EXPIRED" => "expired", "PIKPAK_AUTHORIZATION_DENIED" => "denied", _ => "unknown"
                    };
                    if (error.Code == "PIKPAK_AUTHORIZATION_PENDING") session.Problem = null;
                    else if (error.Code == "PIKPAK_AUTHORIZATION_SLOW_DOWN") SlowDown(session);
                    else if (error.Code == "PIKPAK_AUTHORIZATION_EXPIRED")
                    { session.Status = "expired"; session.DeviceCode = ""; }
                    else if (error.Code == "PIKPAK_AUTHORIZATION_DENIED")
                    { session.Status = "failed"; session.DeviceCode = ""; }
                    // Network, unsupported responses, and not-yet-written credentials are
                    // not a rejection. The private challenge survives until expiry/cancel.
                }
            }
            await PikPakCliStorage.WriteAsync(SessionPath(id), session, CancellationToken.None);
            return View(session);
        }
        finally { _gate.Release(); }
    }

    private static void SlowDown(Session session)
    {
        session.IntervalSeconds = Math.Min(60, session.IntervalSeconds + 5);
        session.NextPollAt = DateTimeOffset.UtcNow.AddSeconds(session.IntervalSeconds);
        session.Problem = null;
    }

    private static List<string>? ReadGrantedScopes(JsonElement result, out bool knownScopeList)
    {
        knownScopeList = false;
        if (!result.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array ||
            scopes.GetArrayLength() > 64 || scopes.EnumerateArray().Any(s => s.ValueKind != JsonValueKind.String)) return null;
        // Store only public permission identifiers, never arbitrary provider fields.
        static bool Known(string scope) => scope is "account" or "files:read" or "files:write" or "files:manage" or "files:purge" or "cloud_download" or "share" or "invite";
        var names = scopes.EnumerateArray().Select(s => s.GetString()!).ToArray();
        knownScopeList = names.All(Known);
        return names.Where(Known)
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private bool HasProfileCredentials(string id)
    {
        var path = PikPakCliProcess.CredentialsPath(Profile(id));
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private static bool CanRecoverCredentials(Session session) => session.Status is "pending" ||
        !session.ManageFilesRequested && session.Status == "failed" && session.Problem is not "PIKPAK_AUTHORIZATION_DENIED";

    private async Task<bool> TryPromote(Session session, CancellationToken ct)
    {
        var status = await _process.RunAsync(Profile(session.Id), ["auth", "status"], ct);
        if (!PikPakCliJson.True(status, "logged_in")) return false;
        var providerIdentity = ReadProviderIdentity(status);
        var account = new Account(session.Id, Guid.NewGuid().ToString("N"), DisplayName(status)) {ProviderIdentity = providerIdentity};
        if (session.ManageFilesRequested)
        {
            if (!session.ManageFilesConsentVerified)
                return await RejectPromotionAsync(session, "PIKPAK_MANAGE_FILES_SCOPE_REQUIRED");
            if (providerIdentity == null)
                return await RejectPromotionAsync(session, "PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED");
            var current = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct);
            if (session.PreviousAccountId != current?.Id || session.PreviousAccountVersion != current?.Version ||
                current != null && session.PreviousProviderIdentity != providerIdentity)
                return await RejectPromotionAsync(session, "PIKPAK_ACCOUNT_CHANGED");
            if (current != null)
            {
                // Only a verified scope upgrade on the same provider identity can
                // retain operation ownership while installing a fresh credential profile.
                account = current with {ProfileId = session.Id, ProviderIdentity = providerIdentity,
                    Name = account.Name ?? current.Name};
            }
            account = account with {CanManageFiles = true};
        }
        await PikPakCliStorage.WriteAsync(AccountPath, account, CancellationToken.None);
        session.Status = "authorized"; session.DeviceCode = ""; session.Problem = null;
        await PikPakCliStorage.WriteAsync(SessionPath(session.Id), session, CancellationToken.None);
        return true;
    }

    private async Task<bool> RejectPromotionAsync(Session session, string problem)
    {
        session.Status = "failed";
        session.DeviceCode = "";
        session.Problem = problem;
        await PikPakCliStorage.WriteAsync(SessionPath(session.Id), session, CancellationToken.None);
        RemoveProfileCredentials(Profile(session.Id));
        return true;
    }

    public async Task CancelAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = await ReadSession(id, ct);
            if (session.Status == "authorized") return;
            session.Status = "cancelled"; session.DeviceCode = "";
            await PikPakCliStorage.WriteAsync(SessionPath(id), session, ct);
            RemoveProfileCredentials(Profile(id));
        }
        finally { _gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var account = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct);
            if (account != null && _leases.GetValueOrDefault(account.Id) != 0) throw new PikPakCliException("PIKPAK_ACCOUNT_IN_USE");
            await CancelActivePending(ct);
            if (account == null) return;
            await _process.RunAsync(Profile(account.EffectiveProfileId), ["auth", "logout"], ct);
            RemoveProfileCredentials(Profile(account.EffectiveProfileId));
            PikPakCliStorage.NoLinks(AccountPath); File.Delete(AccountPath);
        }
        finally { _gate.Release(); }
    }

    internal async Task<T> WithAccountAsync<T>(string? expectedId, string? expectedVersion,
        Func<PikPakCliAccountLease, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        Account account;
        try
        {
            account = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct) ?? throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
            if (expectedId != null && expectedId != account.Id || expectedVersion != null && expectedVersion != account.Version)
                throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
            _leases[account.Id] = _leases.GetValueOrDefault(account.Id) + 1;
        }
        finally { _gate.Release(); }
        try
        {
            return await action(new(account.Id, account.Version, Profile(account.EffectiveProfileId), _process)
            {
                CanManageFiles = account.CanManageFiles, ProviderIdentity = account.ProviderIdentity,
                TrashTemporaryCopiesAfterDownload = account.TrashTemporaryCopiesAfterDownload
            });
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try { if (--_leases[account.Id] == 0) _leases.Remove(account.Id); }
            finally { _gate.Release(); }
        }
    }
    internal async Task<T> WithTransferAsync<T>(PikPakCliAccountLease account, Func<Task<T>> action, CancellationToken ct)
    {
        // The native profile lock serializes individual commands, not the quota → mkdir
        // → restore transaction. Keep one cloud write transaction per account generation.
        // Payload downloads and cached account views do not hold this transaction lock.
        var gate = _transfers.GetOrAdd(account.Id + ":" + account.Version, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    private async Task<Session> ReadSession(string id, CancellationToken ct)
    {
        var session = await PikPakCliStorage.ReadAsync<Session>(SessionPath(id), ct) ?? throw new PikPakCliException("PIKPAK_AUTHORIZATION_NOT_FOUND");
        // Account promotion is the commit point. A crash after that write but before the
        // session acknowledgement must never let cancellation erase active credentials.
        var account = await PikPakCliStorage.ReadAsync<Account>(AccountPath, ct);
        if (account?.EffectiveProfileId == id && session.Status != "authorized")
        {
            session.Status = "authorized"; session.DeviceCode = ""; session.Problem = null;
            await PikPakCliStorage.WriteAsync(SessionPath(id), session, CancellationToken.None);
        }
        return session;
    }
    private async Task CancelActivePending(CancellationToken ct)
    {
        var previous = await PikPakCliStorage.ReadAsync<string>(ActiveAuthorizationPath, ct);
        if (previous == null) return;
        var session = await ReadSession(previous, ct);
        if (session.Status != "pending") return;
        session.Status = "cancelled"; session.DeviceCode = "";
        await PikPakCliStorage.WriteAsync(SessionPath(previous), session, ct);
        RemoveProfileCredentials(Profile(previous));
    }
    private static PikPakCliAuthorizationViewModel View(Session s) => new() {Id = s.Id, Status = s.Status,
        VerificationUri = s.Status == "pending" ? s.VerificationUri : null, UserCode = s.Status == "pending" ? s.UserCode : null,
        ExpiresAt = s.ExpiresAt, IntervalSeconds = s.IntervalSeconds, Problem = s.Problem};
    private static PikPakCliAccountViewModel View(Account account) => new() {IsConfigured = true,
        AccountId = account.Id, AccountVersion = account.Version, DisplayName = account.Name,
        CanManageFiles = account.CanManageFiles, TrashTemporaryCopiesAfterDownload = account.TrashTemporaryCopiesAfterDownload};
    // The official CLI auth status JSON exposes user_id as a stable identity.
    internal static string? ReadProviderIdentity(JsonElement status) =>
        PikPakCliJson.Text(status, "user_id") is {Length: > 0 and <= 160} id && !id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            ? PikPakCliStorage.Hash(id) : null;
    private static string? DisplayName(JsonElement data)
    {
        var name = PikPakCliJson.Text(data, "name") ?? PikPakCliJson.Text(data, "username");
        if (name == null && data.TryGetProperty("user_info", out var user)) name = PikPakCliJson.Text(user, "name") ?? PikPakCliJson.Text(user, "username");
        return name is {Length: > 0 and <= 160} && !name.Any(char.IsControl) ? name : null;
    }
    private static void RemoveProfileCredentials(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path).Where(f => Path.GetFileName(f) is "credentials.json" or ".credentials.json" or "pat.json" || Path.GetFileName(f).StartsWith(".device-", StringComparison.Ordinal)))
        { PikPakCliStorage.NoLinks(file); File.Delete(file); }
    }
    internal sealed record Account(string Id, string Version, string? Name)
    {
        public string? ProfileId { get; init; }
        public string? ProviderIdentity { get; init; }
        public bool CanManageFiles { get; init; }
        public bool TrashTemporaryCopiesAfterDownload { get; init; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string EffectiveProfileId => ProfileId ?? Id;
    }
    internal sealed class Session
    {
        public Session() { }
        public string Id { get; set; } = "";
        public string Status { get; set; } = "pending";
        public string DeviceCode { get; set; } = "";
        public string VerificationUri { get; set; } = "";
        public string UserCode { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset NextPollAt { get; set; }
        public int IntervalSeconds { get; set; }
        public string? Problem { get; set; }
        public string? LastProviderStatus { get; set; }
        public List<string>? GrantedScopes { get; set; }
        public bool ManageFilesRequested { get; set; }
        public bool ManageFilesConsentVerified { get; set; }
        public string? PreviousAccountId { get; set; }
        public string? PreviousAccountVersion { get; set; }
        public string? PreviousProviderIdentity { get; set; }
    }
}

internal sealed record PikPakCliAccountLease(string Id, string Version, string Profile, IPikPakCliProcess Process)
{
    internal bool CanManageFiles { get; init; }
    internal bool TrashTemporaryCopiesAfterDownload { get; init; }
    internal string? ProviderIdentity { get; init; }

    internal Task<JsonElement> Run(System.Collections.Generic.IReadOnlyList<string> arguments, CancellationToken ct,
        bool payload = false, Action? progress = null) => Process.RunAsync(Profile, arguments, ct, payload, progress);
    internal async Task RequireOnline(CancellationToken ct) { await ReadStatusAsync(ct); }
    internal async Task<JsonElement> ReadStatusAsync(CancellationToken ct)
    {
        var status = await Run(["auth", "status"], ct);
        if (!PikPakCliJson.True(status, "logged_in")) throw new PikPakCliException("PIKPAK_ACCOUNT_REQUIRED");
        if (CanManageFiles && (ProviderIdentity == null || PikPakCliAccountService.ReadProviderIdentity(status) != ProviderIdentity))
            throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
        return status;
    }
}
