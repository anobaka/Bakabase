using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.AspNetCore.WebUtilities;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

internal interface IRcloneBrowserAuthorization
{
    Task<RcloneBrowserAuthorizationViewModel> StartAsync(string sessionId, string questionId, string directory, DateTimeOffset expiresAt, CancellationToken ct);
    Task<RcloneBrowserAuthorizationViewModel?> GetAsync(string sessionId, string questionId, CancellationToken ct);
    Task<string> SubmitAsync(string sessionId, string questionId, string callbackUrl, CancellationToken ct);
    Task<RcloneBrowserAuthorizationViewModel?> StopAsync(string sessionId, CancellationToken ct);
}

/// <summary>A single live native loopback authorization. Only rclone exchanges or refreshes OAuth tokens.</summary>
public sealed class RcloneBrowserAuthorizationProcess : IRcloneBrowserAuthorization, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<string>> _executable;
    private readonly BakabaseWebProxy _proxy;
    private readonly HttpClient _loopback;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Entry? _active;
    private static readonly Uri CallbackEndpoint = new("http://127.0.0.1:53682/");

    public RcloneBrowserAuthorizationProcess(RcloneService dependency, BakabaseWebProxy proxy)
        : this(async ct => { await dependency.EnsureReadyAsync(ct); return dependency.Executable; }, proxy) { }
    internal RcloneBrowserAuthorizationProcess(Func<CancellationToken, Task<string>> executable,
        BakabaseWebProxy proxy, HttpMessageHandler? loopbackHandler = null)
    {
        _executable = executable; _proxy = proxy;
        _loopback = new(loopbackHandler ?? new SocketsHttpHandler {UseProxy = false, AllowAutoRedirect = false, UseCookies = false})
            {Timeout = TimeSpan.FromSeconds(10)};
    }

    public async Task<RcloneBrowserAuthorizationViewModel> StartAsync(string sessionId, string questionId, string directory,
        DateTimeOffset expiresAt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is { } existing)
            {
                Refresh(existing);
                if (existing.Status is "awaitingCallback" or "exchanging")
                {
                    if (existing.SessionId == sessionId && existing.QuestionId == questionId) return View(existing);
                    throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_BUSY");
                }
                await DisposeEntry(existing); _active = null;
            }
            if (expiresAt <= DateTimeOffset.UtcNow) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_EXPIRED");
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(a => a.Port == 53682))
                throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_PORT_IN_USE");
            var executable = await _executable(ct);
            if (expiresAt <= DateTimeOffset.UtcNow) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_EXPIRED");
            var info = StartInfo(executable, directory);
            CloudToolProxy.ApplyToEnvironment(info, _proxy.ForDownloadTool(DownloadToolProxyTargets.Rclone, ThirdPartyId.OneDrive),
                new Uri("https://graph.microsoft.com"));
            var entry = new Entry(sessionId, questionId, expiresAt, directory, new Process {StartInfo = info});
            _active = entry;
            try
            {
                if (!entry.Process.Start()) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_START_FAILED");
                entry.Started = true; entry.Process.StandardInput.Close();
                var remaining = expiresAt - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_EXPIRED");
                entry.Lifetime.CancelAfter(remaining);
                entry.KillRegistration = entry.Lifetime.Token.Register(() => Kill(entry));
                entry.Result = Collect(entry);
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct, entry.Lifetime.Token);
                startup.CancelAfter(TimeSpan.FromSeconds(20));
                var first = await Task.WhenAny(entry.LoopbackUrl.Task, entry.Result).WaitAsync(startup.Token);
                if (first == entry.Result) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_PROCESS_EXITED");
                var nativeEntry = await entry.LoopbackUrl.Task;
                var query = QueryHelpers.ParseQuery(new Uri(nativeEntry).Query);
                entry.NativeState = query["state"].ToString();
                using var response = await _loopback.GetAsync(nativeEntry, HttpCompletionOption.ResponseHeadersRead, startup.Token);
                if (response.StatusCode != HttpStatusCode.TemporaryRedirect || response.Headers.Location is not {IsAbsoluteUri: true} location)
                    throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_START_FAILED");
                entry.AuthorizationUrl = ValidateAuthorizationUrl(location, entry.NativeState);
                Refresh(entry);
                if (entry.Status != "awaitingCallback") throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_PROCESS_EXITED");
                return View(entry);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            { entry.Status = "cancelled"; await DisposeEntry(entry); throw; }
            catch (Exception error) when (error is RcloneRemoteException or OperationCanceledException or HttpRequestException or IOException or System.ComponentModel.Win32Exception)
            {
                entry.Problem = error is RcloneRemoteException known ? known.Code : "RCLONE_BROWSER_AUTHORIZATION_START_FAILED";
                entry.Status = entry.Problem == "RCLONE_BROWSER_AUTHORIZATION_EXPIRED" ? "expired" : "failed";
                await DisposeEntry(entry); return View(entry);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<RcloneBrowserAuthorizationViewModel?> GetAsync(string sessionId, string questionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { } entry || entry.SessionId != sessionId || entry.QuestionId != questionId) return null;
            Refresh(entry);
            if (entry.Status is "expired" or "failed") await DisposeEntry(entry);
            return View(entry);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> SubmitAsync(string sessionId, string questionId, string callbackUrl, CancellationToken ct)
    {
        Entry entry; IReadOnlyDictionary<string, string> values; CancellationToken lifetimeToken;
        await _gate.WaitAsync(ct);
        try
        {
            entry = _active ?? throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_NOT_RUNNING");
            if (entry.SessionId != sessionId || entry.QuestionId != questionId)
                throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_QUESTION_CHANGED");
            Refresh(entry);
            if (entry.Submitted) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_ALREADY_SUBMITTED");
            if (entry.Status != "awaitingCallback") throw new RcloneRemoteException(entry.Problem ?? "RCLONE_BROWSER_AUTHORIZATION_NOT_RUNNING");
            values = ValidateCallbackUrl(callbackUrl, entry.NativeState);
            ct.ThrowIfCancellationRequested();
            // From this point a lost browser request must not cause an automatic replay.
            entry.Submitted = true; entry.Status = "exchanging"; entry.AuthorizationUrl = null;
            lifetimeToken = entry.Lifetime.Token;
        }
        finally { _gate.Release(); }
        try
        {
            using var content = new FormUrlEncodedContent(values.Where(v => v.Key != "session_state"));
            using var response = await _loopback.PostAsync(CallbackEndpoint, content, lifetimeToken);
            if (response.StatusCode != HttpStatusCode.OK) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED");
            var output = await entry.Result!.WaitAsync(lifetimeToken);
            lifetimeToken.ThrowIfCancellationRequested();
            var result = ExtractAuthorizationResult(output);
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                lifetimeToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_active, entry) || entry.Status != "exchanging" || entry.ExpiresAt <= DateTimeOffset.UtcNow)
                    throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_NOT_RUNNING");
                entry.Status = "completed"; entry.Problem = null;
            }
            finally { _gate.Release(); }
            return result;
        }
        catch (Exception error) when (error is RcloneRemoteException or OperationCanceledException or HttpRequestException or IOException)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (entry.Status != "cancelled")
                {
                    entry.Status = entry.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "failed";
                    entry.Problem = entry.Status == "expired" ? "RCLONE_BROWSER_AUTHORIZATION_EXPIRED" : "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED";
                }
            }
            finally { _gate.Release(); }
            throw new RcloneRemoteException(entry.Problem ?? "RCLONE_BROWSER_AUTHORIZATION_CALLBACK_FAILED");
        }
        finally { await DisposeEntry(entry); }
    }

    public async Task<RcloneBrowserAuthorizationViewModel?> StopAsync(string sessionId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_active is not { } entry || entry.SessionId != sessionId) return null;
            if (entry.Status is "awaitingCallback" or "exchanging") { entry.Status = "cancelled"; entry.AuthorizationUrl = null; }
            await DisposeEntry(entry); var view = View(entry); _active = null; return view;
        }
        finally { _gate.Release(); }
    }

    internal static ProcessStartInfo StartInfo(string executable, string directory)
    {
        RclonePrivateStorage.Directory(directory);
        var start = new ProcessStartInfo(executable) {UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true};
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        var blob = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, string>
            {{"access_scopes", RcloneAuthorizationService.ReadScopes}}))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        foreach (var arg in new[] {"authorize", "onedrive", blob, "--auth-no-open-browser", "--config", Path.Combine(directory, "rclone.conf"),
                     "--cache-dir", Path.Combine(directory, "cache"), "--temp-dir", Path.Combine(directory, "temporary"), "--ask-password=false", "--log-level", "NOTICE"})
            start.ArgumentList.Add(arg);
        return start;
    }

    internal static IReadOnlyDictionary<string, string> ValidateCallbackUrl(string value, string expectedState)
    {
        if (value == null || value.Length > 65536) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_INVALID");
        value = value.Trim();
        // Microsoft can append an empty fragment marker; fragments carrying data are never callbacks.
        if (value.EndsWith('#')) value = value[..^1];
        var question = value.IndexOf('?');
        if (question < 0 || !value[..question].Equals("http://localhost:53682/", StringComparison.OrdinalIgnoreCase) ||
            value.Any(char.IsControl) || value.Any(char.IsWhiteSpace) || value.Contains('#') || value.Contains('\\') ||
            Regex.IsMatch(value, "%(?![0-9a-fA-F]{2})") || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Fragment.Length != 0)
            throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_INVALID");
        var query = QueryHelpers.ParseQuery(uri.Query);
        var allowed = new HashSet<string>(StringComparer.Ordinal) {"code", "state", "error", "error_description", "error_uri", "session_state"};
        if (query.Any(p => !allowed.Contains(p.Key) || p.Value.Count != 1 || p.Value.ToString().Any(char.IsControl)) || !query.TryGetValue("state", out var state) || string.IsNullOrEmpty(state[0]) ||
            query.ContainsKey("code") == query.ContainsKey("error") || query.TryGetValue("code", out var code) && string.IsNullOrWhiteSpace(code[0]) ||
            query.TryGetValue("error", out var error) && string.IsNullOrWhiteSpace(error[0]))
            throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_CALLBACK_INVALID");
        if (!Equal(state.ToString(), expectedState)) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_STATE_MISMATCH");
        return query.ToDictionary(p => p.Key, p => p.Value.ToString(), StringComparer.Ordinal);
    }

    private static string ValidateAuthorizationUrl(Uri uri, string expectedState)
    {
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (uri.Scheme != "https" || uri.Host != "login.microsoftonline.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/common/oauth2/v2.0/authorize" || query.Any(v => v.Value.Count != 1) ||
            !query.TryGetValue("state", out var state) || !Equal(state.ToString(), expectedState) ||
            query["client_id"] != "b15665d9-eda6-4092-8539-0eec376afd59" || query["redirect_uri"] != "http://localhost:53682/" || query["response_type"] != "code" ||
            !query["scope"].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(s => s, StringComparer.Ordinal)
                .SequenceEqual(RcloneAuthorizationService.ReadScopes.Split(' ').OrderBy(s => s, StringComparer.Ordinal)))
            throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_START_FAILED");
        return uri.AbsoluteUri;
    }

    private static bool Equal(string actual, string expected) => expected.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));

    private static async Task<string> Collect(Entry entry)
    {
        var stdout = ReadOutput(entry); var stderr = ReadLog(entry);
        try
        {
            await entry.Process.WaitForExitAsync(CancellationToken.None);
            var output = await stdout; await stderr;
            if (entry.Process.ExitCode != 0) throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_PROCESS_EXITED");
            return output;
        }
        finally { try { await Task.WhenAll(stdout, stderr); } catch { } }
    }
    private static async Task<string> ReadOutput(Entry entry)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await entry.Process.StandardOutput.ReadAsync(buffer)) > 0)
        {
            if (result.Length + count > 1024 * 1024) { Kill(entry); throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_RESULT_INVALID"); }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    private static async Task ReadLog(Entry entry)
    {
        var buffer = new char[4096]; var line = new StringBuilder(); int count;
        void Inspect()
        {
            var match = Regex.Match(line.ToString(), @"http://127\.0\.0\.1:53682/auth\?state=[A-Za-z0-9_-]{1,4096}");
            if (match.Success) entry.LoopbackUrl.TrySetResult(match.Value);
            line.Clear();
        }
        while ((count = await entry.Process.StandardError.ReadAsync(buffer)) > 0)
            for (var i = 0; i < count; i++) if (buffer[i] == '\n') Inspect(); else if (line.Length < 16384) line.Append(buffer[i]);
        if (line.Length > 0) Inspect();
    }
    private static string ExtractAuthorizationResult(string output)
    {
        const string start = "Paste the following into your remote machine --->";
        const string end = "<---End paste";
        var at = output.IndexOf(start, StringComparison.Ordinal); var stop = output.IndexOf(end, StringComparison.Ordinal);
        if (at < 0 || stop <= at + start.Length || output.IndexOf(start, at + start.Length, StringComparison.Ordinal) >= 0)
            throw new RcloneRemoteException("RCLONE_BROWSER_AUTHORIZATION_RESULT_INVALID");
        var result = output[(at + start.Length)..stop].Trim(); RcloneAuthorizationService.ValidateAuthorizationResult(result); return result;
    }
    private static void Refresh(Entry entry)
    {
        if (entry.Status is not ("awaitingCallback" or "exchanging")) return;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow) { entry.Status = "expired"; entry.Problem = "RCLONE_BROWSER_AUTHORIZATION_EXPIRED"; Kill(entry); }
        else if ((entry.Started && entry.Process.HasExited || entry.Result?.IsCompleted == true) && !entry.Submitted)
        { entry.Status = "failed"; entry.Problem = "RCLONE_BROWSER_AUTHORIZATION_PROCESS_EXITED"; }
        if (entry.Status != "awaitingCallback") entry.AuthorizationUrl = null;
    }
    private static RcloneBrowserAuthorizationViewModel View(Entry entry) => new()
        {Status = entry.Status, AuthorizationUrl = entry.Status == "awaitingCallback" ? entry.AuthorizationUrl : null, ExpiresAt = entry.ExpiresAt, Problem = entry.Problem};
    private static void Kill(Entry entry)
    { try { if (entry.Started && !entry.Process.HasExited) entry.Process.Kill(true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    private static Task DisposeEntry(Entry entry)
    {
        lock (entry.DisposeGate) return entry.DisposeTask ??= DisposeEntryCore(entry);
    }
    private static async Task DisposeEntryCore(Entry entry)
    {
        entry.Lifetime.Cancel(); Kill(entry);
        if (entry.Started) await entry.Process.WaitForExitAsync(CancellationToken.None);
        if (entry.Result != null) { try { await entry.Result; } catch { } }
        entry.KillRegistration.Dispose(); entry.Lifetime.Dispose(); entry.Process.Dispose();
        try { ClearStage(entry.Directory); } catch (IOException) { /* Session cleanup retries inside its private root. */ }
    }
    internal static void ClearStage(string directory)
    {
        RcloneRemoteEngine.RejectLinks(directory);
        if (!System.IO.Directory.Exists(directory)) return;
        foreach (var child in System.IO.Directory.EnumerateFileSystemEntries(directory))
        {
            RcloneRemoteEngine.RejectLinks(child);
            if (System.IO.Directory.Exists(child)) ClearStage(child); else File.Delete(child);
        }
        System.IO.Directory.Delete(directory);
    }
    public async ValueTask DisposeAsync()
    { await _gate.WaitAsync(); try { if (_active != null) { await DisposeEntry(_active); _active = null; } _loopback.Dispose(); } finally { _gate.Release(); } }
    private sealed class Entry(string sessionId, string questionId, DateTimeOffset expiresAt, string directory, Process process)
    {
        public string SessionId { get; } = sessionId;
        public string QuestionId { get; } = questionId;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public Process Process { get; } = process;
        public string Directory { get; } = directory;
        public bool Started { get; set; }
        public object DisposeGate { get; } = new();
        public Task? DisposeTask { get; set; }
        public bool Submitted { get; set; }
        public string Status { get; set; } = "awaitingCallback";
        public string NativeState { get; set; } = "";
        public string? AuthorizationUrl { get; set; }
        public string? Problem { get; set; }
        public CancellationTokenSource Lifetime { get; } = new();
        public CancellationTokenRegistration KillRegistration { get; set; }
        public TaskCompletionSource<string> LoopbackUrl { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string>? Result { get; set; }
    }
}
