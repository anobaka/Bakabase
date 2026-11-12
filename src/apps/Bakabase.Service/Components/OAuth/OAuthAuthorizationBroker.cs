using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.OAuth;

internal sealed record OAuthAuthorizationView(string Status, string? AuthorizationUrl, DateTimeOffset ExpiresAt, string? Problem);
internal sealed record OAuthAuthorizationGrant(OAuthClientSnapshot Client, string TokenJson)
{ public override string ToString() => "OAuth grant (redacted)"; }

/// <summary>Ephemeral authorization-code/PKCE ownership. Workflow and download execution do not handle OAuth.</summary>
public sealed class OAuthAuthorizationBroker : IAsyncDisposable
{
    private readonly string _directory;
    private readonly OAuthProviderRegistry _registry;
    private readonly Func<IOAuthProvider, HttpClient> _clientFactory;
    private readonly IDisposable? _sharedTransport;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, Entry> _consumers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Entry> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private static readonly HashSet<string> CallbackFields = new(StringComparer.Ordinal)
        {"state", "code", "error", "error_description", "error_uri", "session_state"};

    public OAuthAuthorizationBroker(AppService app, OAuthProviderRegistry registry, BakabaseWebProxy proxy)
    {
        _directory = Path.Combine(app.AppDataDirectory, "oauth-authorizations"); _registry = registry; _now = () => DateTimeOffset.UtcNow;
        _clientFactory = provider => new HttpClient(new SocketsHttpHandler
        { UseProxy = true, Proxy = proxy.ForDownloadTool(provider.ProxyToolId, provider.ProxyThirdPartyId),
            AllowAutoRedirect = false, UseCookies = false }) {Timeout = TimeSpan.FromSeconds(45)};
    }
    internal OAuthAuthorizationBroker(string directory, OAuthProviderRegistry registry, HttpMessageHandler handler,
        Func<DateTimeOffset>? now = null)
    {
        _directory = Path.GetFullPath(directory); _registry = registry; _sharedTransport = handler;
        _clientFactory = _ => new HttpClient(handler, false) {Timeout = TimeSpan.FromSeconds(45)};
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<OAuthAuthorizationView> StartAsync(string consumer, string questionId, OAuthClientSnapshot snapshot,
        DateTimeOffset expiresAt, Func<OAuthAuthorizationGrant, CancellationToken, Task> complete, CancellationToken ct)
    {
        await _startGate.WaitAsync(ct);
        try
        {
            if (_consumers.TryGetValue(consumer, out var previous))
            {
                lock (previous.Sync)
                {
                    Refresh(previous);
                    if (previous.QuestionId == questionId && previous.Status is "awaitingCallback" or "exchanging") return View(previous);
                }
                await StopAsync(consumer, ct);
                _states.TryRemove(previous.StateHash, out _);
                lock (previous.Sync) { previous.Retired = true; DisposeRetired(previous); }
            }
            var provider = _registry.Get(snapshot.ProviderId);
            if (snapshot.AuthEndpoint != provider.GetAuthorizationEndpoint(snapshot.TenantId).AbsoluteUri ||
                snapshot.TokenEndpoint != provider.GetTokenEndpoint(snapshot.TenantId).AbsoluteUri || snapshot.Scopes != provider.Scopes ||
                !Uri.TryCreate(snapshot.CallbackUrl, UriKind.Absolute, out var callback) || callback.Scheme != "https" ||
                callback.UserInfo.Length != 0 || callback.Query.Length != 0 || callback.Fragment.Length != 0 || callback.AbsolutePath != provider.CallbackPath)
                throw new OAuthAuthorizationException("OAUTH_CONFIGURATION_INVALID");
            var expiry = expiresAt < _now().AddMinutes(15) ? expiresAt : _now().AddMinutes(15);
            if (expiry <= _now()) throw new OAuthAuthorizationException("OAUTH_SESSION_EXPIRED");
            var state = Random(32); var verifier = Random(64);
            var entry = new Entry(consumer, questionId, snapshot, expiry, state, verifier, complete);
            entry.AuthorizationUrl = QueryHelpers.AddQueryString(snapshot.AuthEndpoint, new Dictionary<string, string?>
            {
                {"client_id", snapshot.ClientId}, {"response_type", "code"}, {"redirect_uri", snapshot.CallbackUrl},
                {"response_mode", "query"}, {"scope", snapshot.Scopes}, {"state", state},
                {"code_challenge", Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))}, {"code_challenge_method", "S256"}
            });
            await WriteReceipt(entry, ct);
            _consumers[consumer] = entry; _states[entry.StateHash] = entry;
            var remaining = expiry - _now();
            if (remaining <= TimeSpan.Zero) { await StopAsync(consumer, CancellationToken.None); throw new OAuthAuthorizationException("OAUTH_SESSION_EXPIRED"); }
            entry.Lifetime.CancelAfter(remaining);
            return View(entry);
        }
        finally { _startGate.Release(); }
    }

    internal Task<OAuthAuthorizationView?> GetAsync(string consumer, string questionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_consumers.TryGetValue(consumer, out var entry) || entry.QuestionId != questionId) return Task.FromResult<OAuthAuthorizationView?>(null);
        lock (entry.Sync) { Refresh(entry); return Task.FromResult<OAuthAuthorizationView?>(View(entry)); }
    }

    internal async Task<OAuthAuthorizationView?> StopAsync(string consumer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_consumers.TryGetValue(consumer, out var entry)) return null;
        OAuthAuthorizationView view;
        lock (entry.Sync)
        {
            Refresh(entry);
            if (entry.Status is "awaitingCallback" or "exchanging") { entry.Status = "cancelled"; entry.Problem = "OAUTH_AUTHORIZATION_CANCELLED"; }
            entry.AuthorizationUrl = null; entry.Snapshot = null; entry.Verifier = null; view = View(entry);
            if (!entry.LifetimeDisposed) entry.Lifetime.Cancel();
        }
        await WriteReceipt(entry, CancellationToken.None);
        return view;
    }

    /// <summary>Only the exact registered public callback calls this. All redemption inputs remain server-owned.</summary>
    public async Task<bool> CompleteAsync(string providerId, string requestPath, IQueryCollection query, CancellationToken requestAborted)
    {
        var provider = _registry.Get(providerId);
        if (requestPath != provider.CallbackPath || query.Count is < 2 or > 7 ||
            query.Any(p => !CallbackFields.Contains(p.Key) || p.Value.Count != 1 || p.Value.ToString().Length > 16384 || p.Value.ToString().Any(char.IsControl)) ||
            !query.TryGetValue("state", out var state) || !Regex.IsMatch(state.ToString(), @"\A[A-Za-z0-9_-]{43}\z") ||
            query.ContainsKey("code") == query.ContainsKey("error") || query.TryGetValue("code", out var incomingCode) && string.IsNullOrWhiteSpace(incomingCode) ||
            query.TryGetValue("error", out var incomingError) && string.IsNullOrWhiteSpace(incomingError))
            throw new OAuthAuthorizationException("OAUTH_CALLBACK_INVALID");
        if (!_states.TryGetValue(Hash(state.ToString()), out var entry) || entry.ProviderId != providerId)
            throw new OAuthAuthorizationException("OAUTH_STATE_INVALID");
        OAuthClientSnapshot snapshot; string verifier;
        lock (entry.Sync)
        {
            Refresh(entry);
            if (entry.Status != "awaitingCallback" || entry.Snapshot == null || entry.Verifier == null)
                throw new OAuthAuthorizationException(entry.Problem ?? "OAUTH_CALLBACK_ALREADY_USED");
            if (new Uri(entry.Snapshot.CallbackUrl).AbsolutePath != requestPath)
                throw new OAuthAuthorizationException("OAUTH_CALLBACK_INVALID");
            snapshot = entry.Snapshot; verifier = entry.Verifier;
            entry.Status = "exchanging"; entry.AuthorizationUrl = null; entry.InFlight = true;
        }
        // Persist the one-use claim before the token request. PKCE, raw state, code, tokens and
        // credentials are never stored in this receipt. No authorization is restored after restart.
        try
        {
            await WriteReceipt(entry, CancellationToken.None);
            if (query.ContainsKey("error")) throw new OAuthAuthorizationException("OAUTH_AUTHORIZATION_DENIED");
            var code = query["code"].ToString();
            if (string.IsNullOrWhiteSpace(code)) throw new OAuthAuthorizationException("OAUTH_CALLBACK_INVALID");
            entry.Lifetime.Token.ThrowIfCancellationRequested();
            var token = await Exchange(snapshot, verifier, code, entry.Lifetime.Token);
            lock (entry.Sync)
            {
                Refresh(entry);
                if (entry.Status != "exchanging") throw new OAuthAuthorizationException(entry.Problem ?? "OAUTH_AUTHORIZATION_CANCELLED");
            }
            await entry.Complete(new(snapshot, token), entry.Lifetime.Token);
            lock (entry.Sync)
            {
                Refresh(entry); entry.Lifetime.Token.ThrowIfCancellationRequested();
                if (entry.Status != "exchanging") throw new OAuthAuthorizationException(entry.Problem ?? "OAUTH_AUTHORIZATION_CANCELLED");
                entry.Status = "completed"; entry.Problem = null;
            }
            return true;
        }
        catch (Exception error)
        {
            lock (entry.Sync)
            {
                if (entry.Status is not ("cancelled" or "expired"))
                {
                    entry.Status = "failed";
                    entry.Problem = error is OAuthAuthorizationException known ? known.Code : "OAUTH_TOKEN_EXCHANGE_FAILED";
                }
            }
            return false;
        }
        finally
        {
            lock (entry.Sync) { entry.Snapshot = null; entry.Verifier = null; entry.AuthorizationUrl = null; entry.InFlight = false; DisposeRetired(entry); }
            await WriteReceipt(entry, CancellationToken.None);
        }
    }

    private async Task<string> Exchange(OAuthClientSnapshot snapshot, string verifier, string code, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, snapshot.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"client_id", snapshot.ClientId}, {"client_secret", snapshot.ClientSecret}, {"grant_type", "authorization_code"},
                {"code", code}, {"redirect_uri", snapshot.CallbackUrl}, {"code_verifier", verifier}, {"scope", snapshot.Scopes}
            })
        };
        // Never send browser Origin/cookies/Authorization or follow a redirect carrying credentials.
        using var client = _clientFactory(_registry.Get(snapshot.ProviderId));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new OAuthAuthorizationException("OAUTH_TOKEN_EXCHANGE_FAILED");
        using var body = new MemoryStream(); await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > 256 * 1024) throw new OAuthAuthorizationException("OAUTH_TOKEN_RESPONSE_INVALID");
            await body.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        using var parsed = JsonDocument.Parse(body.ToArray()); var token = parsed.RootElement;
        if (token.ValueKind != JsonValueKind.Object) throw new OAuthAuthorizationException("OAUTH_TOKEN_RESPONSE_INVALID");
        string? Text(string key) => token.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        var access = Text("access_token"); var refresh = Text("refresh_token");
        if (token.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) ||
            Text("token_type") is not ("Bearer" or "bearer") || !token.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt64(out var seconds) || seconds is < 1 or > 86400)
            throw new OAuthAuthorizationException("OAUTH_TOKEN_RESPONSE_INVALID");
        if (token.TryGetProperty("scope", out var scope) && (scope.ValueKind != JsonValueKind.String ||
            !_registry.Get(snapshot.ProviderId).AreReturnedScopesAllowed(scope.GetString()!)))
            throw new OAuthAuthorizationException("OAUTH_TOKEN_SCOPE_MISMATCH");
        // Allowlisted rclone token shape only. The private rclone profile becomes the refresh authority.
        return JsonSerializer.Serialize(new {access_token = access, token_type = "Bearer", refresh_token = refresh,
            expiry = _now().AddSeconds(seconds).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)});
    }

    internal async Task CleanupExpiredAsync(CancellationToken ct)
    {
        foreach (var entry in _states.Values.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            lock (entry.Sync) Refresh(entry);
            if (entry.ExpiresAt <= _now()) await WriteReceipt(entry, ct);
            if (entry.ExpiresAt.AddMinutes(5) <= _now())
            {
                _states.TryRemove(entry.StateHash, out _);
                ((ICollection<KeyValuePair<string, Entry>>)_consumers).Remove(new(entry.Consumer, entry));
                lock (entry.Sync) { entry.Retired = true; DisposeRetired(entry); }
            }
        }
    }
    private void Refresh(Entry entry)
    {
        if (entry.ExpiresAt <= _now() && entry.Status is "awaitingCallback" or "exchanging")
        {
            entry.Status = "expired"; entry.Problem = "OAUTH_SESSION_EXPIRED"; entry.AuthorizationUrl = null;
            entry.Snapshot = null; entry.Verifier = null; entry.Lifetime.Cancel();
        }
    }
    private async Task WriteReceipt(Entry entry, CancellationToken ct)
    {
        await entry.ReceiptGate.WaitAsync(ct);
        try
        {
            OAuthPrivateStorage.Directory(_directory);
            string receipt;
            lock (entry.Sync) receipt = JsonSerializer.Serialize(new {entry.Id, entry.Consumer, entry.QuestionId, entry.ProviderId, entry.StateHash, entry.ExpiresAt, entry.Status});
            await OAuthPrivateStorage.WriteAsync(Path.Combine(_directory, entry.Id + ".json"), receipt, ct);
        }
        finally { entry.ReceiptGate.Release(); }
    }
    private static void DisposeRetired(Entry entry)
    { if (entry.Retired && !entry.InFlight && !entry.LifetimeDisposed) { entry.LifetimeDisposed = true; entry.Lifetime.Dispose(); } }
    private static OAuthAuthorizationView View(Entry entry) => new(entry.Status, entry.Status == "awaitingCallback" ? entry.AuthorizationUrl : null, entry.ExpiresAt, entry.Problem);
    private static string Random(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(state)));
    public async ValueTask DisposeAsync()
    {
        foreach (var consumer in _consumers.Keys) await StopAsync(consumer, CancellationToken.None);
        foreach (var entry in _states.Values) lock (entry.Sync) { entry.Retired = true; DisposeRetired(entry); }
        _sharedTransport?.Dispose();
    }
    private sealed class Entry(string consumer, string questionId, OAuthClientSnapshot snapshot, DateTimeOffset expiresAt,
        string state, string verifier, Func<OAuthAuthorizationGrant, CancellationToken, Task> complete)
    {
        public object Sync { get; } = new();
        public SemaphoreSlim ReceiptGate { get; } = new(1, 1);
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Consumer { get; } = consumer;
        public string QuestionId { get; } = questionId;
        public string ProviderId { get; } = snapshot.ProviderId;
        public string StateHash { get; } = Hash(state);
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public OAuthClientSnapshot? Snapshot { get; set; } = snapshot;
        public string? Verifier { get; set; } = verifier;
        public string? AuthorizationUrl { get; set; }
        public string Status { get; set; } = "awaitingCallback";
        public string? Problem { get; set; }
        public CancellationTokenSource Lifetime { get; } = new();
        public bool InFlight { get; set; }
        public bool Retired { get; set; }
        public bool LifetimeDisposed { get; set; }
        public Func<OAuthAuthorizationGrant, CancellationToken, Task> Complete { get; } = complete;
    }
}
