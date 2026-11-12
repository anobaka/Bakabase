using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jint;

namespace Bakabase.Service.Components.SourceAvailability.Probes;

public interface IGoFileWebsiteToken
{
    Task<string> GenerateAsync(string anonymousCredential, CancellationToken ct);
}

/// <summary>Runs only GoFile's current, bounded public signing script in a fresh JS-only engine.
/// No host objects/delegates, CLR, network, filesystem, modules or string compilation are exposed.
/// The official frontend expires scripts after six hours; we expire after one and never use stale
/// scripts when refresh fails. See https://gofile.io/js/core/wt.js (reviewed 2026-10-26).</summary>
public sealed class GoFileWebsiteToken : IGoFileWebsiteToken
{
    internal const string UserAgent = "Mozilla/5.0 Bakabase-SourceAvailability/1.0";
    internal const string Language = "en-US";
    internal const int MaximumScriptBytes = 65536;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _script;
    private DateTimeOffset _expiresAt;
    private readonly IHttpClientFactory _clients;
    private readonly TimeProvider _time;
    public GoFileWebsiteToken(IHttpClientFactory clients) : this(clients, TimeProvider.System) { }
    internal GoFileWebsiteToken(IHttpClientFactory clients, TimeProvider time) { _clients = clients; _time = time; }

    public async Task<string> GenerateAsync(string anonymousCredential, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_script == null || _expiresAt <= _time.GetUtcNow())
            {
                _script = null;
                using var client = _clients.CreateClient(GoFileAvailabilityProbe.ClientName);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://gofile.io/js/wt.obf.js");
                request.Headers.CacheControl = new() {NoCache = true};
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != HttpStatusCode.OK) throw new GoFileProbeProtocolException();
                var body = await PixeldrainAvailabilityProbe.ReadBounded(response.Content, MaximumScriptBytes, ct);
                var script = Encoding.UTF8.GetString(body);
                // Validate with a non-secret token before retaining a changed provider script.
                Evaluate(script, new string('a', 32), ct);
                _script = script;
                _expiresAt = _time.GetUtcNow().AddHours(1);
            }
            try { return Evaluate(_script, anonymousCredential, ct); }
            catch { _script = null; throw; }
        }
        finally { _gate.Release(); }
    }

    internal static string Evaluate(string script, string credential, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(script) > MaximumScriptBytes || credential.Length > 4096)
            throw new GoFileProbeProtocolException();
        try
        {
            using var engine = new Engine(options =>
            {
                options.LimitMemory(32_000_000).MaxStatements(400_000).LimitRecursion(32)
                    .TimeoutInterval(TimeSpan.FromSeconds(1)).CancellationToken(ct);
                options.Host.StringCompilationAllowed = false;
                options.Interop.Enabled = false;
                options.Interop.AllowGetType = false;
                options.Constraints.MaxArraySize = 100_000;
                options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(100);
            });
            engine.Execute("const navigator = Object.freeze(" + JsonSerializer.Serialize(new {userAgent = UserAgent, language = Language}) + ");");
            engine.Execute(script);
            var value = engine.Invoke("generateWT", credential);
            // Never unwrap promises, invoke getters or convert arbitrary objects returned by changed code.
            if (!value.IsString()) throw new GoFileProbeProtocolException();
            var token = value.AsString();
            if (!Regex.IsMatch(token, @"\A[0-9a-f]{64}\z")) throw new GoFileProbeProtocolException();
            return token;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            ct.ThrowIfCancellationRequested();
            throw new GoFileProbeProtocolException();
        }
    }
}

internal sealed class GoFileProbeProtocolException : Exception
{
    public GoFileProbeProtocolException() : base("SOURCE_PROBE_PROTOCOL_CHANGED") { }
}
