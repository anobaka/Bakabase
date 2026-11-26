using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.PikPakCli;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

internal interface IPikPakCliProcess
{
    Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
        bool payload = false, Action? observeProgress = null);

    Task PermanentlyDeleteFilesAsync(string profile, string providerIdentity, IReadOnlyList<string> fileIds, CancellationToken ct) =>
        throw new PikPakCliException("PIKPAK_PURGE_UNAVAILABLE");

    /// <summary>Read current status without waiting for another command on the credential profile.</summary>
    Task<JsonElement?> TryReadStatusAsync(string profile, CancellationToken ct) => Task.FromResult<JsonElement?>(null);
}

/// <summary>The official CLI owns OAuth and refreshes the protected profile. Never logs its raw output,
/// token-bearing arguments or environment, and never inherits a user's CLI credentials.</summary>
internal sealed class PikPakCliProcess(PikPakCliService dependency, BakabaseWebProxy proxy) : IPikPakCliProcess
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profiles = new(StringComparer.Ordinal);
    public async Task<JsonElement> RunAsync(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
        bool payload = false, Action? observeProgress = null)
    {
        // Refreshable OAuth files belong to the official CLI. Serialize commands for the
        // same profile without blocking account metadata or other authorization sessions.
        var gate = _profiles.GetOrAdd(profile, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try { return await RunCore(profile, arguments, ct, payload, observeProgress); }
        finally { gate.Release(); }
    }

    private readonly PikPakCliPurgeClient _purge = new(proxy);

    public async Task PermanentlyDeleteFilesAsync(string profile, string providerIdentity, IReadOnlyList<string> fileIds, CancellationToken ct)
    {
        var gate = _profiles.GetOrAdd(profile, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // auth status may refresh the token and is the authoritative user identity.
            // Keep the same profile locked until the Drive request finishes.
            var status = await RunCore(profile, ["auth", "status"], ct, payload: false);
            await _purge.DeleteFilesAsync(profile, providerIdentity, status, fileIds, ct);
        }
        finally { gate.Release(); }
    }

    public async Task<JsonElement?> TryReadStatusAsync(string profile, CancellationToken ct)
    {
        var gate = _profiles.GetOrAdd(profile, _ => new(1, 1));
        if (!await gate.WaitAsync(0, ct)) return null;
        try { return await RunCore(profile, ["auth", "status"], ct, payload: false); }
        finally { gate.Release(); }
    }

    private async Task<JsonElement> RunCore(string profile, IReadOnlyList<string> arguments, CancellationToken ct,
        bool payload, Action? observeProgress = null)
    {
        await dependency.EnsureReadyAsync(ct);
        PikPakCliStorage.Directory(profile);
        var start = CreateStartInfo(dependency.Executable, profile, arguments);
        if (payload) ConfigurePayloadStorage(start, arguments);
        CloudToolProxy.ApplyToEnvironment(start, proxy.ForDownloadTool("pikpak-cli", ThirdPartyId.PikPak),
            new Uri("https://api-drive.mypikpak.com"));
        try { return await RunProcessAsync(start, arguments, ct, payload, observeProgress); }
        finally
        {
            // The CLI may refresh credentials on any call. Parent directories are private too.
            PikPakCliStorage.SecureFiles(profile);
        }
    }

    internal static async Task<JsonElement> RunProcessAsync(ProcessStartInfo start, IReadOnlyList<string> arguments,
        CancellationToken ct, bool payload, Action? observeProgress = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!payload) deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process {StartInfo = start};
        if (!process.Start()) throw new PikPakCliException("PIKPAK_CLI_START_FAILED");
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        using var cancel = deadline.Token.Register(Kill);
        var exited = process.WaitForExitAsync(CancellationToken.None);
        var writeObservation = payload && arguments.Count > 0 && arguments[0] == "download" && observeProgress != null
            ? LocalFileProcessIo.ObserveStorageWritesAsync(process.Id, exited, observeProgress)
            : Task.CompletedTask;
        var output = Read(process.StandardOutput, Kill, 32 * 1024 * 1024);
        var errors = Read(process.StandardError, Kill, 256 * 1024, truncate: true);
        process.StandardInput.Close();
        try
        {
            await exited.WaitAsync(deadline.Token);
            var stdout = await output; var stderr = await errors;
            deadline.Token.ThrowIfCancellationRequested();
            var authorization = arguments.Count >= 2 && arguments[0] == "auth" && arguments[1] == "login";
            JsonElement result;
            try { using var parsed = JsonDocument.Parse(stdout); result = parsed.RootElement.Clone(); }
            catch (JsonException)
            {
                if (process.ExitCode == 0 && payload) return JsonSerializer.SerializeToElement(new {success = true});
                throw new PikPakCliException(process.ExitCode == 0 ? "PIKPAK_CLI_RESPONSE_INVALID" : Classify(null, stderr, authorization))
                    {Evidence = FailureEvidence(arguments, process.ExitCode, "invalid", null)};
            }
            if (result.ValueKind != JsonValueKind.Object) throw new PikPakCliException("PIKPAK_CLI_RESPONSE_INVALID");
            if (process.ExitCode != 0 || result.TryGetProperty("error", out _))
                throw new PikPakCliException(Classify(result, stderr, authorization))
                    {Evidence = FailureEvidence(arguments, process.ExitCode, "json", result)};
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new PikPakCliException("PIKPAK_NETWORK_UNAVAILABLE")
            {Evidence = FailureEvidence(arguments, null, "timeout", null)}; }
        finally
        {
            Kill();
            await exited;
            await writeObservation;
            try { await Task.WhenAll(output, errors); } catch { }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string profile, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(executable) {UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = profile};
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("PIKPAK_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["PIKPAK_DIR"] = profile;
        start.Environment["PIKPAK_CREDENTIALS"] = CredentialsPath(profile);
        start.Environment["PIKPAK_PAT_PATH"] = Path.Combine(profile, "pat.json");
        start.Environment["PIKPAK_NO_LOG"] = "1";
        start.Environment["PIKPAK_NO_SKILL_SYNC"] = "1";
        start.Environment["PIKPAK_NO_UPDATE_NOTIFIER"] = "1";
        start.Environment["NO_COLOR"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("-F"); start.ArgumentList.Add("json");
        return start;
    }

    internal static string CredentialsPath(string profile)
    {
        var current = Path.Combine(profile, "credentials.json");
        var legacy = Path.Combine(profile, ".credentials.json");
        PikPakCliStorage.NoLinks(current); PikPakCliStorage.NoLinks(legacy);
        // CLI 0.6 reads PIKPAK_CREDENTIALS. Earlier integrations supplied an
        // unsupported _PATH variable, so PIKPAK_DIR isolated its dotted default.
        // Reuse that exact private file; do not lose a prior login or adopt both.
        if (File.Exists(current) && File.Exists(legacy)) throw new PikPakCliException("PIKPAK_STATE_INVALID");
        return File.Exists(legacy) ? legacy : current;
    }

    internal static void ConfigurePayloadStorage(ProcessStartInfo start, IReadOnlyList<string> arguments)
    {
        var outputIndex = arguments.ToList().IndexOf("--output");
        if (outputIndex < 0 || outputIndex + 1 >= arguments.Count || !Path.IsPathFullyQualified(arguments[outputIndex + 1]))
            throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
        // Native range/download scratch belongs on the selected destination volume too.
        // AppData contains only credentials and receipts, never payload temporary files.
        var working = Path.GetDirectoryName(arguments[outputIndex + 1])!;
        PikPakCliStorage.NoLinks(working);
        var temporary = Path.Combine(working, "temporary"); PikPakCliStorage.Directory(temporary);
        start.WorkingDirectory = working;
        foreach (var name in new[] {"TMPDIR", "TMP", "TEMP"}) start.Environment[name] = temporary;
    }

    internal static string Classify(JsonElement? result, string diagnostic = "", bool authorization = false)
    {
        var code = ""; var reason = diagnostic;
        if (result is {ValueKind: JsonValueKind.Object} value && value.TryGetProperty("error", out var error))
        {
            code = PikPakCliJson.Text(error, "code") ?? "";
            reason += " " + (PikPakCliJson.Text(error, "reason") ?? "") + " " + (PikPakCliJson.Text(error, "message") ?? "");
        }
        if (authorization)
        {
            if (code == "authorization_pending") return "PIKPAK_AUTHORIZATION_PENDING";
            if (code == "slow_down") return "PIKPAK_AUTHORIZATION_SLOW_DOWN";
            if (code == "expired_token") return "PIKPAK_AUTHORIZATION_EXPIRED";
            if (code == "access_denied") return "PIKPAK_AUTHORIZATION_DENIED";
        }
        if (code is "UNAUTHENTICATED" or "TOKEN_EXPIRED" || reason.Contains("not logged in", StringComparison.OrdinalIgnoreCase)) return "PIKPAK_ACCOUNT_REQUIRED";
        if (code is "ACCESS_DENIED" or "PERMISSION_DENIED" || reason.Contains("scope", StringComparison.OrdinalIgnoreCase)) return "PIKPAK_PERMISSION_REQUIRED";
        if (reason.Contains("space", StringComparison.OrdinalIgnoreCase) || reason.Contains("storage quota", StringComparison.OrdinalIgnoreCase)) return "PIKPAK_STORAGE_QUOTA_EXCEEDED";
        if (reason.Contains("quota", StringComparison.OrdinalIgnoreCase) || reason.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || code == "RATE_LIMITED") return "PIKPAK_TRANSFER_QUOTA_EXCEEDED";
        if (reason.Contains("pass code", StringComparison.OrdinalIgnoreCase) || code is "PASS_CODE_ERROR" or "PASS_CODE_EMPTY") return "PIKPAK_ACCESS_CODE_REQUIRED";
        if (code is "SHARE_EXPIRED" or "SHARE_DELETED" or "SHARE_NOT_FOUND") return "PIKPAK_SHARE_EXPIRED";
        if (code is "CAPTCHA_REQUIRED" or "CAPTCHA_INVALID") return "PIKPAK_VERIFICATION_REQUIRED";
        if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase) || reason.Contains("connection", StringComparison.OrdinalIgnoreCase) || code == "transport_error") return "PIKPAK_NETWORK_UNAVAILABLE";
        return "PIKPAK_CLI_REQUEST_FAILED";
    }

    internal static PikPakCliFailureEvidence FailureEvidence(IReadOnlyList<string> arguments, int? exitCode,
        string responseKind, JsonElement? result)
    {
        var phase = arguments.Count >= 2 && arguments[0] == "share" && arguments[1] == "restore" ? "share.restore" : "command";
        string? providerCode = null;
        if (result is {ValueKind: JsonValueKind.Object} value && value.TryGetProperty("error", out var error))
        {
            var code = PikPakCliJson.Text(error, "code");
            // Do not retain arbitrary provider text, response bodies, URLs, or account tokens.
            providerCode = code is "UNAUTHENTICATED" or "UNAUTHORIZED" or "TOKEN_EXPIRED" or "ACCESS_DENIED" or
                "PERMISSION_DENIED" or "RATE_LIMITED" or "PASS_CODE_ERROR" or "PASS_CODE_EMPTY" or "SHARE_EXPIRED" or
                "SHARE_DELETED" or "SHARE_NOT_FOUND" or "FILE_NOT_FOUND" or "CAPTCHA_REQUIRED" or "CAPTCHA_INVALID" or "transport_error" ? code : null;
        }
        return new(phase, exitCode, responseKind is "json" or "invalid" or "timeout" ? responseKind : "unknown", providerCode);
    }

    private static async Task<string> Read(StreamReader reader, Action kill, int maximum, bool truncate = false)
    {
        var result = new StringBuilder(); var buffer = new char[8192]; int read;
        while ((read = await reader.ReadAsync(buffer)) != 0)
        {
            if (result.Length + read > maximum && !truncate) { kill(); throw new PikPakCliException("PIKPAK_CLI_OUTPUT_LIMIT"); }
            if (result.Length < maximum) result.Append(buffer, 0, Math.Min(read, maximum - result.Length));
        }
        return result.ToString();
    }
}
