using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

/// <summary>Official out-of-band OAuth. Only an opaque profile generation reaches callers;
/// the one-use code travels on stdin and is never retained or automatically replayed.</summary>
public sealed class TeraBoxAccountService : ICloudDownloadAccountResolver
{
    internal const string AuthorizationUri = "https://www.terabox.com/weboauth/ai?client_id=no0UBVGmwye33e-rq_VTIPf-dlMuZkYn";
    private readonly string _root;
    private readonly ITeraBoxCliProcess _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _leases = new(StringComparer.Ordinal);
    public TeraBoxAccountService(AppService app, TeraBoxCliService dependency, BakabaseWebProxy proxy)
        : this(Path.Combine(app.AppDataDirectory, "cloud-downloads", "terabox"), new TeraBoxCliProcess(dependency, proxy)) { }
    internal TeraBoxAccountService(string root, ITeraBoxCliProcess process) { _root = Path.GetFullPath(root); _process = process; }
    internal string Root => _root;
    public string AccountProvider => "terabox";
    private string AccountPath => Path.Combine(_root, "account.json");
    private string ActivePath => Path.Combine(_root, "active-authorization.json");
    private string SessionPath(string id) { CheckId(id); return Path.Combine(_root, "sessions", id + ".json"); }
    internal string Config(string id)
    { CheckId(id); var path = Path.Combine(_root, "profiles", id, "terabox", "config.json"); TeraBoxStorage.NoLinks(path); return path; }
    private static void CheckId(string id) { if (!Guid.TryParseExact(id, "N", out _)) throw new TeraBoxException("TERABOX_ACCOUNT_INVALID"); }
    public async Task<TeraBoxAccountViewModel> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var account = await TeraBoxStorage.ReadAsync<Account>(AccountPath, ct);
            return account != null && !File.Exists(Config(account.Id)) ? new() {Problem = "TERABOX_ACCOUNT_REQUIRED"} : View(account);
        }
        finally { _gate.Release(); }
    }
    private static TeraBoxAccountViewModel View(Account? account) => account == null
        ? new() {Problem = "TERABOX_ACCOUNT_REQUIRED"}
        : new() {IsConfigured = true, AccountId = account.Id, AccountVersion = account.Version};
    public async Task<CloudAccountReference> ResolveAccountAsync(string? accountId, string? accountVersion, CancellationToken ct) =>
        await WithAccountAsync(accountId, accountVersion, account => Task.FromResult(new CloudAccountReference(account.Id, account.Version)), ct);
    public Task<TeraBoxAccountViewModel> ResolveConfiguredAccountAsync(string? accountId, CancellationToken ct) =>
        WithAccountAsync(accountId, null, account => Task.FromResult(new TeraBoxAccountViewModel
            {IsConfigured = true, AccountId = account.Id, AccountVersion = account.Version}), ct);

    public async Task<TeraBoxAuthorizationViewModel> StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireIdle();
            var previous = await TeraBoxStorage.ReadAsync<string>(ActivePath, ct);
            if (previous != null)
            {
                var session = await ReadSession(previous, ct);
                if (session.Status == "pending")
                { session.Status = "cancelled"; await TeraBoxStorage.WriteAsync(SessionPath(previous), session, ct); }
            }
            var current = new Session {Id = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)};
            await TeraBoxStorage.WriteAsync(SessionPath(current.Id), current, ct);
            await TeraBoxStorage.WriteAsync(ActivePath, current.Id, ct);
            return View(current);
        }
        finally { _gate.Release(); }
    }

    public async Task<TeraBoxAuthorizationViewModel?> GetActiveAuthorizationAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var id = await TeraBoxStorage.ReadAsync<string>(ActivePath, ct);
            if (id == null) return null;
            var session = await ReadSession(id, ct);
            // A response may be lost after the CLI wrote its config. A read-only
            // local whoami can promote it without exchanging the one-use code again.
            if (session.Status == "pending" && session.Submitted && File.Exists(Config(id)))
            {
                RequireIdle();
                try { if (await TryPromote(session, ct)) return View(session); }
                catch (TeraBoxException error) { session.Problem = error.Code; }
            }
            return session.Status == "pending" && session.ExpiresAt <= DateTimeOffset.UtcNow
                ? View(session) with {Status = "expired", AuthorizationUri = null} : View(session);
        }
        finally { _gate.Release(); }
    }

    public async Task<TeraBoxAuthorizationViewModel> CompleteAsync(string id, string code, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = await ReadSession(id, ct);
            if (await TeraBoxStorage.ReadAsync<string>(ActivePath, ct) != id)
                throw new TeraBoxException("TERABOX_AUTHORIZATION_CHANGED");
            if (session.Status == "authorized") return View(session);
            if (session.Status != "pending") throw new TeraBoxException("TERABOX_AUTHORIZATION_CHANGED");
            RequireIdle();
            if (session.Submitted)
            {
                if (File.Exists(Config(id)) && await TryPromote(session, ct)) return View(session);
                throw new TeraBoxException("TERABOX_AUTHORIZATION_REVIEW_REQUIRED");
            }
            if (session.ExpiresAt <= DateTimeOffset.UtcNow) throw new TeraBoxException("TERABOX_AUTHORIZATION_EXPIRED");
            if (string.IsNullOrWhiteSpace(code) || code.Length > 4096 || code.Any(c => c is < '!' or > '~'))
                throw new TeraBoxException("TERABOX_AUTHORIZATION_CODE_INVALID");
            await _process.EnsureReadyAsync(ct);
            session.Submitted = true;
            await TeraBoxStorage.WriteAsync(SessionPath(id), session, ct);
            try
            {
                await _process.RunAsync(Config(id), ["login", "--code-stdin"], ct, code);
                if (!await TryPromote(session, ct)) session.Problem = "TERABOX_AUTHORIZATION_AWAITING_CREDENTIALS";
            }
            catch (TeraBoxException error) { session.Problem = error.Code; }
            catch (OperationCanceledException)
            {
                session.Problem = "TERABOX_AUTHORIZATION_REVIEW_REQUIRED";
                await TeraBoxStorage.WriteAsync(SessionPath(id), session, CancellationToken.None);
                throw;
            }
            await TeraBoxStorage.WriteAsync(SessionPath(id), session, CancellationToken.None);
            return View(session);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> TryPromote(Session session, CancellationToken ct)
    {
        var current = await TeraBoxStorage.ReadAsync<Account>(AccountPath, ct);
        if (current?.Id != session.Id)
        {
            var status = await _process.RunAsync(Config(session.Id), ["whoami"], ct);
            if (!TeraBoxJson.True(status, "logged_in")) return false;
            if (!File.Exists(Config(session.Id))) return false;
            await TeraBoxStorage.WriteAsync(AccountPath, new Account(session.Id, Guid.NewGuid().ToString("N")), CancellationToken.None);
        }
        session.Status = "authorized"; session.Problem = null;
        await TeraBoxStorage.WriteAsync(SessionPath(session.Id), session, CancellationToken.None);
        return true;
    }

    public async Task CancelAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = await ReadSession(id, ct);
            var account = await TeraBoxStorage.ReadAsync<Account>(AccountPath, ct);
            if (account?.Id == id || session.Status == "authorized") return;
            session.Status = "cancelled";
            await TeraBoxStorage.WriteAsync(SessionPath(id), session, ct);
            RemoveConfig(id);
        }
        finally { _gate.Release(); }
    }
    public async Task LogoutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            RequireIdle();
            var account = await TeraBoxStorage.ReadAsync<Account>(AccountPath, ct);
            if (account != null) { RemoveConfig(account.Id); File.Delete(AccountPath); }
            var activeId = await TeraBoxStorage.ReadAsync<string>(ActivePath, ct);
            if (activeId != null)
            {
                var pending = await ReadSession(activeId, ct);
                if (pending.Status == "pending")
                { pending.Status = "cancelled"; await TeraBoxStorage.WriteAsync(SessionPath(activeId), pending, ct); RemoveConfig(activeId); }
            }
            // Keep append-only operation receipts and authorization history.
        }
        finally { _gate.Release(); }
    }
    private void RemoveConfig(string id)
    { var config = Config(id); TeraBoxStorage.NoLinks(config); if (File.Exists(config)) File.Delete(config); }
    private void RequireIdle() { if (_leases.Values.Any(n => n > 0)) throw new TeraBoxException("TERABOX_ACCOUNT_IN_USE"); }
    private async Task<Session> ReadSession(string id, CancellationToken ct) =>
        await TeraBoxStorage.ReadAsync<Session>(SessionPath(id), ct) ?? throw new TeraBoxException("TERABOX_AUTHORIZATION_NOT_FOUND");
    private static TeraBoxAuthorizationViewModel View(Session session) => new()
    {Id = session.Id, Status = session.Status, ExpiresAt = session.ExpiresAt, Problem = session.Problem,
        AuthorizationUri = session.Status == "pending" ? AuthorizationUri : null};

    internal async Task<T> WithAccountAsync<T>(string? accountId, string? accountVersion, Func<AccountLease, Task<T>> action, CancellationToken ct)
    {
        Account account;
        await _gate.WaitAsync(ct);
        try
        {
            account = await TeraBoxStorage.ReadAsync<Account>(AccountPath, ct) ?? throw new TeraBoxException("TERABOX_ACCOUNT_REQUIRED");
            if (accountId != null && accountId != account.Id || accountVersion != null && accountVersion != account.Version)
                throw new TeraBoxException("TERABOX_ACCOUNT_CHANGED");
            if (!File.Exists(Config(account.Id))) throw new TeraBoxException("TERABOX_ACCOUNT_REQUIRED");
            _leases[account.Id] = _leases.GetValueOrDefault(account.Id) + 1;
        }
        finally { _gate.Release(); }
        try { return await action(new AccountLease(account.Id, account.Version, Config(account.Id), _process)); }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try { _leases[account.Id]--; } finally { _gate.Release(); }
        }
    }
    internal sealed record Account(string Id, string Version);
    private sealed class Session
    {
        public required string Id {get; init;}
        public string Status {get; set;} = "pending";
        public required DateTimeOffset ExpiresAt {get; init;}
        public bool Submitted {get; set;}
        public string? Problem {get; set;}
    }
    internal sealed class AccountLease(string id, string version, string config, ITeraBoxCliProcess process)
    {
        internal string Id => id;
        internal string Version => version;
        internal Task<JsonElement> Run(IReadOnlyList<string> arguments, CancellationToken ct, string? payloadDirectory = null) =>
            process.RunAsync(config, arguments, ct, payloadDirectory: payloadDirectory);
        internal async Task RequireOnline(CancellationToken ct)
        {
            // whoami only inspects the native local config. The documented quota
            // command makes a protected, read-only request using this OAuth token.
            // A full or over-quota account can still download public shares.
            var quota = await Run(["quota"], ct);
            if (TeraBoxJson.Number(quota, "total") == null || TeraBoxJson.Number(quota, "used") == null ||
                TeraBoxJson.Number(quota, "free") == null)
                throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
        }
    }
}
