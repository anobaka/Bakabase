using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

internal interface ITeraBoxCliProcess
{
    Task EnsureReadyAsync(CancellationToken ct) => Task.CompletedTask;
    Task<JsonElement> RunAsync(string config, IReadOnlyList<string> arguments, CancellationToken ct,
        string? standardInput = null, string? payloadDirectory = null);
}

internal sealed class TeraBoxCliProcess(TeraBoxCliService dependency, BakabaseWebProxy proxy) : ITeraBoxCliProcess
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _profiles = new(StringComparer.Ordinal);
    private readonly TeraBoxDownloadFailureProbe _downloadFailures = new(proxy);
    public async Task EnsureReadyAsync(CancellationToken ct)
    {
        try { await dependency.EnsureReadyAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch { throw new TeraBoxException("TERABOX_DEPENDENCY_REQUIRED"); }
    }
    public async Task<JsonElement> RunAsync(string config, IReadOnlyList<string> arguments, CancellationToken ct,
        string? standardInput = null, string? payloadDirectory = null)
    {
        var gate = _profiles.GetOrAdd(config, _ => new(1, 1)); await gate.WaitAsync(ct);
        try { return await RunCore(config, arguments, ct, standardInput, payloadDirectory); }
        finally { gate.Release(); }
    }
    private async Task<JsonElement> RunCore(string config, IReadOnlyList<string> arguments, CancellationToken ct,
        string? standardInput, string? payloadDirectory)
    {
        await EnsureReadyAsync(ct);
        TeraBoxStorage.NoLinks(config); TeraBoxStorage.Directory(Path.GetDirectoryName(config)!);
        var start = CreateStartInfo(dependency.Executable, config, arguments, payloadDirectory);
        CloudToolProxy.ApplyToEnvironment(start, proxy.ForDownloadTool(DownloadToolProxyTargets.TeraBoxCli, ThirdPartyId.TeraBox),
            new Uri("https://www.terabox.com"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (payloadDirectory == null) deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process {StartInfo = start};
        if (!process.Start()) throw new TeraBoxException("TERABOX_CLI_START_FAILED");
        void Kill() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        using var cancellation = deadline.Token.Register(Kill);
        var output = Read(process.StandardOutput, Kill, 32 * 1024 * 1024);
        var errors = Read(process.StandardError, Kill, 256 * 1024, true);
        try
        {
            if (standardInput != null) await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var stdout = await output; var stderr = await errors;
            deadline.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 && TeraBoxDownloadFailureProbe.IsNativeDownloadDenied(arguments, stderr))
                throw new TeraBoxException(await _downloadFailures.ClassifyAsync(config, arguments, payloadDirectory, stdout, deadline.Token));
            // Official 1.1.0 login prints a human receipt even under --json. It is
            // never parsed or surfaced; account promotion separately checks whoami.
            if (process.ExitCode == 0 && arguments.SequenceEqual(new[] {"login", "--code-stdin"}))
                return JsonSerializer.SerializeToElement(new { });
            JsonElement result;
            try { result = ParseResponse(arguments.FirstOrDefault() ?? "", stdout); }
            catch (TeraBoxException parseError) when (parseError.Code == "TERABOX_CLI_RESPONSE_INVALID" && process.ExitCode != 0)
            { throw new TeraBoxException(Classify(null, stderr)); }
            if (result.ValueKind != JsonValueKind.Object) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
            if (process.ExitCode != 0 || result.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) && error.ToString().Length > 0 ||
                result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False ||
                result.TryGetProperty("errno", out var errno) && (errno.ValueKind != JsonValueKind.Number || !errno.TryGetInt32(out var n) || n != 0))
                throw new TeraBoxException(Classify(result, stderr));
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TeraBoxException("TERABOX_NETWORK_UNAVAILABLE"); }
        finally
        {
            Kill(); await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(output, errors); } catch { }
            TeraBoxStorage.SecureFiles(Path.GetDirectoryName(config)!);
        }
    }
    private const int MaximumResponseLength = 32 * 1024 * 1024;
    private const int MaximumDiagnosticJsonLength = 8 * 1024 * 1024;
    private static readonly Regex ShareListParameters = new(
        // Official 1.1.0 omits the root value for --dir; root listings use 1.
        @"\A\[DEBUG\] share/list params: shorturl=[A-Za-z0-9_-]{1,160}, sekey=[^\r\n]*, dir=[^\r\n]*, root=[01]?, page=[1-9][0-9]{0,5}, num=[1-9][0-9]{0,3}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex ShareDownloadParameters = new(
        @"\A\[DEBUG\] share/download URL params \(no token\): shareid=[1-9][0-9]{0,19}&uk=[1-9][0-9]{0,19}&sign=[^&\r\n]*&timestamp=[0-9]{1,20}&fid_list=\[[0-9]+(?:,[0-9]+)*\]&sekey=[^\r\n]*\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>Official 1.1.0 writes these diagnostics to stdout even with --json.
    /// They can contain signed URLs/cookies: discard them in memory, never log or
    /// expose them. Unknown text, another command's diagnostics and extra formal
    /// responses remain protocol errors rather than selecting a convenient object.</summary>
    internal static JsonElement ParseResponse(string command, string output)
    {
        if (output.Length > MaximumResponseLength) throw new TeraBoxException("TERABOX_CLI_OUTPUT_LIMIT");
        var remaining = output.AsSpan().Trim(); var count = 0;
        while (remaining.StartsWith("[DEBUG] ", StringComparison.Ordinal))
        {
            if (++count > 16) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
            var newline = remaining.IndexOf('\n');
            if (newline < 0 || newline > MaximumDiagnosticJsonLength) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
            var line = remaining[..newline].TrimEnd('\r');
            if (!KnownDiagnostic(command, line)) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
            remaining = remaining[(newline + 1)..].TrimStart();
        }
        try
        {
            using var json = JsonDocument.Parse(remaining.ToString());
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID");
            return json.RootElement.Clone();
        }
        catch (JsonException) { throw new TeraBoxException("TERABOX_CLI_RESPONSE_INVALID"); }
    }

    internal static bool KnownDiagnostic(string command, ReadOnlySpan<char> line)
    {
        if (line.Length <= 16 * 1024)
        {
            if (command == "share-list" && ShareListParameters.IsMatch(line)) return true;
            if (command == "share-download" && ShareDownloadParameters.IsMatch(line)) return true;
        }
        string? prefix = null;
        if ((command is "share-info" or "share-list" or "share-download") && line.StartsWith("[DEBUG] shorturlinfo response: ", StringComparison.Ordinal))
            prefix = "[DEBUG] shorturlinfo response: ";
        else if (command == "share-list" && line.StartsWith("[DEBUG] share/list response: ", StringComparison.Ordinal))
            prefix = "[DEBUG] share/list response: ";
        else if (command == "share-download" && line.StartsWith("[DEBUG] share/download response: ", StringComparison.Ordinal))
            prefix = "[DEBUG] share/download response: ";
        if (prefix == null) return false;
        try { using var json = JsonDocument.Parse(line[prefix.Length..].ToString()); return json.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
    internal static ProcessStartInfo CreateStartInfo(string executable, string config, IReadOnlyList<string> arguments, string? payloadDirectory = null)
    {
        if (!Path.IsPathFullyQualified(config)) throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
        var configDirectory = Path.GetDirectoryName(config)!;
        // v1.1.0's login/whoami use the default resolver even when --config is
        // provided. Pin both mechanisms to exactly the same private file.
        if (Path.GetFileName(config) != "config.json" || Path.GetFileName(configDirectory) != "terabox")
            throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
        var profile = Path.GetDirectoryName(configDirectory)!;
        var start = new ProcessStartInfo(executable) {UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = configDirectory};
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("TERABOX_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["XDG_CONFIG_HOME"] = profile;
        start.Environment["APPDATA"] = profile;
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(profile, "cache");
        start.Environment["NO_COLOR"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        start.ArgumentList.Add("--no-check-update"); start.ArgumentList.Add("--json");
        if (payloadDirectory != null)
        {
            if (!Path.IsPathFullyQualified(payloadDirectory)) throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
            TeraBoxStorage.NoLinks(payloadDirectory);
            var temporary = Path.Combine(payloadDirectory, ".temporary"); TeraBoxStorage.PayloadDirectory(temporary);
            start.WorkingDirectory = payloadDirectory;
            foreach (var name in new[] {"TMPDIR", "TMP", "TEMP"}) start.Environment[name] = temporary;
        }
        return start;
    }
    internal static string Classify(JsonElement? data, string diagnostic = "")
    {
        var reason = diagnostic;
        if (data is {ValueKind: JsonValueKind.Object} value)
        {
            reason += " " + (TeraBoxJson.Text(value, "message") ?? "") + " " + (TeraBoxJson.Text(value, "error") ?? "");
            if (value.TryGetProperty("errno", out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var errno))
            {
                if (errno == -3) return "TERABOX_ACCESS_CODE_REQUIRED";
                if (errno == -6) return "TERABOX_ACCOUNT_REQUIRED";
                if (errno == -7) return "TERABOX_PERMISSION_REQUIRED";
                if (errno == 112) return "TERABOX_SIGNATURE_EXPIRED";
            }
        }
        var lower = reason.ToLowerInvariant();
        if (lower.Contains("please run") && lower.Contains("terabox login") || lower.Contains("not logged in") || lower.Contains("token expired")) return "TERABOX_ACCOUNT_REQUIRED";
        if (lower.Contains("share") && (lower.Contains("expired") || lower.Contains("deleted"))) return "TERABOX_SHARE_UNAVAILABLE";
        if (lower.Contains("password required") || lower.Contains("wrong password")) return "TERABOX_ACCESS_CODE_REQUIRED";
        if (lower.Contains("timeout") || lower.Contains("timed out") || lower.Contains("connection") || lower.Contains("no such host")) return "TERABOX_NETWORK_UNAVAILABLE";
        return "TERABOX_CLI_REQUEST_FAILED";
    }
    private static async Task<string> Read(StreamReader reader, Action kill, int limit, bool truncate = false)
    {
        var result = new StringBuilder(); var buffer = new char[8192]; int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            if (result.Length + count > limit && !truncate) { kill(); throw new TeraBoxException("TERABOX_CLI_OUTPUT_LIMIT"); }
            if (result.Length < limit) result.Append(buffer, 0, Math.Min(count, limit - result.Length));
        }
        return result.ToString();
    }
}
