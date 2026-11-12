using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

/// <summary>One bounded content-metadata request, using a reusable anonymous website session.
/// No recursive listing, payload fetch, purchase, transfer or user account is involved.</summary>
public sealed class GoFileAvailabilityProbe(IHttpClientFactory clients, GoFileAnonymousSession sessions,
    IGoFileWebsiteToken websiteToken) : ISourceAvailabilityProbe
{
    public const string ClientName = "Bakabase.SourceAvailability.GoFile";
    public int CacheVersion => 1;
    public bool CanHandle(CloudSource source) => CloudSourceClassifier.Classify(source.Url) == "gofile";

    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.IdnHost is not ("gofile.io" or "www.gofile.io") ||
            !Regex.IsMatch(uri.AbsolutePath, @"\A/d/[A-Za-z0-9_-]{6,128}/?\z") || uri.Fragment.Length > 0 ||
            source.AccessCode?.Length > 1024)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_URL_INVALID");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            string? rejected = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var session = await sessions.GetAsync(rejected, deadline.Token);
                var signature = await websiteToken.GenerateAsync(session.Credential, deadline.Token);
                using var client = clients.CreateClient(ClientName);
                var id = uri.AbsolutePath.TrimEnd('/')[3..];
                var endpoint = "https://api.gofile.io/contents/" + id + "?page=1&pageSize=1";
                if (!string.IsNullOrEmpty(source.AccessCode)) endpoint += "&password=" +
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.AccessCode))).ToLowerInvariant();
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Credential);
                request.Headers.TryAddWithoutValidation("X-Website-Token", signature);
                request.Headers.TryAddWithoutValidation("X-BL", GoFileWebsiteToken.Language);
                request.Headers.Referrer = new Uri("https://gofile.io/");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                using var body = await ReadAsync(response, deadline.Token);
                var code = String(body.RootElement, "status");
                if (response.StatusCode == HttpStatusCode.Unauthorized && code is "error-token" or "error-wrongToken" && attempt == 0)
                { rejected = session.Credential; continue; }
                return Classify(response.StatusCode, body.RootElement, id);
            }
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_SESSION_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (GoFileProbeProtocolException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_PROTOCOL_CHANGED"); }
        catch (GoFileSessionException error) { return error.Result; }
        catch (HttpRequestException)
        { return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_NETWORK_ERROR"); }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED"); }
        catch (IOException)
        { return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_SESSION_UNAVAILABLE"); }
    }

    internal static SourceAvailabilityResult Classify(HttpStatusCode http, JsonElement json, string contentId)
    {
        var code = String(json, "status");
        // Transport/auth responses take precedence. Only the provider's validated envelope
        // at the fixed content endpoint proves absence, never an HTML/generic HTTP404.
        if ((int)http == 429 || (int)http >= 500 || code == "error-rateLimit")
            return Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT");
        if ((int)http is 401 or 403)
            return Result(code is "error-token" or "error-wrongToken" or "error-notPremium" ? SourceAvailabilityStatuses.Unknown : SourceAvailabilityStatuses.AccessRequired,
                code == "error-notPremium" ? "SOURCE_PROBE_PROTOCOL_CHANGED" :
                code is "error-token" or "error-wrongToken" ? "SOURCE_PROBE_SESSION_UNAVAILABLE" : "SOURCE_ACCESS_REQUIRED");
        if ((http is HttpStatusCode.OK or HttpStatusCode.NotFound) && code == "error-notFound")
            return Result(SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_NOT_FOUND", "error-notFound");
        if (code is "error-passwordRequired" or "error-passwordWrong")
            return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_CODE_REQUIRED");
        if (http != HttpStatusCode.OK || code != "ok" || !json.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED");
        if (String(data, "id") != contentId && String(data, "code") != contentId)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_RESPONSE_UNRECOGNIZED");
        // Match the official website's gate order: password/private must not become expiry.
        if (data.TryGetProperty("canAccess", out var access) && access.ValueKind == JsonValueKind.False)
        {
            if (IsTrue(data, "password")) return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_CODE_REQUIRED");
            if (data.TryGetProperty("public", out var visibility) && visibility.ValueKind == JsonValueKind.False)
                return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
            if (IsTrue(data, "expire") || data.TryGetProperty("expire", out var expiry) && expiry.ValueKind == JsonValueKind.Number && expiry.TryGetInt64(out var stamp) && stamp > 0)
                return Result(SourceAvailabilityStatuses.ConfirmedUnavailable, "SOURCE_SHARE_EXPIRED", "expire");
            return Result(SourceAvailabilityStatuses.AccessRequired, "SOURCE_ACCESS_REQUIRED");
        }
        if (String(data, "type") is not ("file" or "folder") ||
            !data.TryGetProperty("canAccess", out access) || access.ValueKind != JsonValueKind.True)
            return Result(SourceAvailabilityStatuses.Unknown, "SOURCE_METADATA_INCOMPLETE");
        return Result(SourceAvailabilityStatuses.Available, "SOURCE_METADATA_AVAILABLE", "ok");
    }

    internal static string? String(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool IsTrue(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    internal static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct) =>
        JsonDocument.Parse(await PixeldrainAvailabilityProbe.ReadBounded(response.Content, 128 * 1024, ct));
    internal static SourceAvailabilityResult Result(string status, string reason, string? providerStatus = null) =>
        AvailabilityProbeResults.Create("gofile.guest-metadata", status, reason,
            new() {["scope"] = "anonymous", ["response"] = providerStatus ?? reason});
}

/// <summary>Separates the website's anonymous-session bootstrap from read-only content checks.
/// One private session survives process restarts; concurrency and rejection backoff prevent
/// an account per link. Existing signed-in user accounts are never read or changed.</summary>
public sealed class GoFileAnonymousSession(IHttpClientFactory clients, SourceAvailabilityStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SourceAvailabilitySession? _current;
    private DateTimeOffset _blockedUntil;
    private bool _loaded;
    internal async Task<SourceAvailabilitySession> GetAsync(string? rejected, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loaded) { _current = await store.SessionAsync("gofile.guest", ct); _loaded = true; }
            if (_current != null && ValidCredential(_current.Credential) && _current.Credential != rejected) return _current;
            if (_blockedUntil > DateTimeOffset.UtcNow || rejected != null && _current?.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5))
                throw new GoFileSessionException(GoFileAvailabilityProbe.Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_SESSION_UNAVAILABLE"));
            _blockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            using var client = clients.CreateClient(GoFileAvailabilityProbe.ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.gofile.io/accounts")
                {Content = new StringContent("{}", Encoding.UTF8, "application/json")};
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            using var body = await GoFileAvailabilityProbe.ReadAsync(response, ct);
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                throw new GoFileSessionException(GoFileAvailabilityProbe.Result(SourceAvailabilityStatuses.TemporarilyUnavailable, "SOURCE_RATE_OR_SERVICE_LIMIT"));
            var data = body.RootElement;
            var token = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var session)
                ? GoFileAvailabilityProbe.String(session, "token") : null;
            if (response.StatusCode != HttpStatusCode.OK || GoFileAvailabilityProbe.String(data, "status") != "ok" || !ValidCredential(token))
                throw new GoFileSessionException(GoFileAvailabilityProbe.Result(SourceAvailabilityStatuses.Unknown, "SOURCE_PROBE_SESSION_UNAVAILABLE"));
            _current = new(token!, DateTimeOffset.UtcNow);
            // Once the provider created a session, preserve its credential even if the caller
            // cancelled just after receiving it. A subsequent check must not mint another one.
            await store.SaveSessionAsync("gofile.guest", _current, CancellationToken.None);
            return _current;
        }
        finally { _gate.Release(); }
    }
    internal static bool ValidCredential(string? value) => value is {Length: >= 16 and <= 4096} &&
        Regex.IsMatch(value, @"\A[A-Za-z0-9._~-]+\z");
}

internal sealed class GoFileSessionException(SourceAvailabilityResult result) : Exception("SOURCE_PROBE_SESSION_UNAVAILABLE")
{
    public SourceAvailabilityResult Result { get; } = result;
}
