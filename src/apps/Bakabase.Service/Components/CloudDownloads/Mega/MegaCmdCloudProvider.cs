using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.MegaCmd;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Mega;

/// <summary>Official SDK download/decryption through an exclusively owned MEGAcmd server.
/// Remote content is never imported into an account. Uncertain/partial outcomes cannot create
/// a second generation or invoke MEGAcmd's automatic "(NUM)" duplicate naming.</summary>
public sealed class MegaCmdCloudProvider(AppService app, MegaCmdService dependency, BakabaseWebProxy proxy)
    : ICloudDownloadProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Root => Path.Combine(app.AppDataDirectory, "cloud-downloads", "megacmd");
    private string Profile => Path.Combine(Root, "profile");
    private string NativeProfile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".megaCmd");
    public string ProviderId => "mega";
    public string MethodId => "mega.megacmd";

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        if (accountId != null) return new(false, "MEGA_PUBLIC_ACCOUNT_NOT_REQUIRED");
        try
        {
            await dependency.EnsureReadyAsync(ct);
            // Managed Linux packages are verified and cannot auto-start an unrelated server.
            // Other systems retain dependency/manual-install information until equally isolated.
            if (!OperatingSystem.IsLinux()) return new(false, "MEGA_ISOLATED_RUNTIME_REQUIRED");
            MegaCmdProfile.Ensure(NativeProfile, Profile, false);
            if (NeedsServerReview(_gate, Path.Combine(Root, "uncertain-server.json"))) return new(false, "MEGA_SERVER_REVIEW_REQUIRED");
            ValidateProxy(ResolveProxy());
            return new(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return new(false, e.Message is "MEGA_PROFILE_CONFLICT" or "MEGA_PROXY_UNSUPPORTED" ? e.Message : "MEGA_DEPENDENCY_REQUIRED"); }
    }

    internal static bool NeedsServerReview(SemaphoreSlim gate, string markerPath)
    {
        // A running managed transfer writes this marker before issuing get. A second
        // transfer can queue behind its owner and will recheck the marker under the lock.
        if (!gate.Wait(0)) return false;
        try { return File.Exists(markerPath); }
        finally { gate.Release(); }
    }

    private sealed record LocalFile(string Path, long Size, string Sha256);
    private sealed record Receipt(string Identity, string State, IReadOnlyList<LocalFile>? Files = null);

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        var publicLink = MegaPublicLink.Require(request.Source.Url, request.Source.AccessCode);
        if (request.Source.ProviderId != ProviderId || request.Source.Kind != "share" ||
            request.RemoteEntry != null || request.AccountId != null || request.Source.AccountId != null ||
            string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new CloudProviderException("MEGA_REQUEST_INVALID");
        var ready = await CheckReadyAsync(null, ct);
        if (!ready.Available) throw new CloudProviderException(ready.Problem!);
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Root);
            using var owner = new FileStream(Path.Combine(Root, "engine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // Readiness was checked before waiting; another operation may have fenced this
            // shared upstream profile while we were queued.
            if (File.Exists(Path.Combine(Root, "uncertain-server.json"))) throw new CloudProviderException("MEGA_SERVER_REVIEW_REQUIRED");
            var id = CloudOperationService.Key(request.OperationId);
            var identity = CloudOperationService.Key(CloudSourceIdentity.Key(request.Source), Path.GetFullPath(request.OutputDirectory));
            var receiptFile = Path.Combine(Root, id + ".json");
            if (File.Exists(receiptFile))
            {
                var previous = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptFile, ct));
                if (previous?.Identity != identity) throw new CloudProviderException("MEGA_OPERATION_CHANGED");
                if (previous.State == "completed" && previous.Files is {Count: > 0} files)
                {
                    await progress(new(null, Stage: "Verifying"));
                    if (await VerifyAsync(request.OutputDirectory, files, ct))
                    {
                        await progress(new(100, files.Sum(f => f.Size), files.Sum(f => f.Size), "Completed"));
                        return new(files.Select(f => Path.Combine(request.OutputDirectory, f.Path)).ToList());
                    }
                }
                throw new CloudProviderException("MEGA_DOWNLOAD_REVIEW_REQUIRED");
            }
            RequireEmptyOutput(request.OutputDirectory);
            MegaCmdProfile.Ensure(NativeProfile, Profile, true);
            var socket = "bakabase-" + CloudOperationService.Key(Root)[..16];
            var processDirectory = Path.Combine(Root, "process");
            // This directory is the client's entire PATH and cwd. A copied fallback server
            // here would defeat the no-autostart guarantee when the owned process dies.
            RequireEmptyOutput(processDirectory);
            Directory.CreateDirectory(processDirectory);
            using var server = new Process {StartInfo = Start(dependency.ServerExecutable, socket, [])};
            if (!server.Start()) throw new CloudProviderException("MEGA_SERVER_UNAVAILABLE");
            server.StandardInput.Close();
            // Drain both pipes without retaining upstream output containing share keys or paths.
            var serverOut = server.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var serverError = server.StandardError.BaseStream.CopyToAsync(Stream.Null);
            var getStarted = false; var safeExit = false;
            try
            {
                using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                startup.CancelAfter(TimeSpan.FromSeconds(15));
                var connected = false;
                while (!server.HasExited && !connected)
                {
                    try { await Command(server, socket, ["version"], startup.Token); connected = true; }
                    catch (CloudProviderException) { await Task.Delay(200, startup.Token); }
                }
                if (!connected) throw new CloudProviderException("MEGA_SERVER_UNAVAILABLE");
                var settings = ResolveProxy();
                ValidateProxy(settings);
                // Proxy credentials are never included in errors/logs. Arguments are passed directly, no shell.
                var proxyArgs = new List<string> {"proxy", settings?.Url ?? "--none"};
                if (settings?.Username != null) {proxyArgs.Add("--username=" + settings.Username); proxyArgs.Add("--password=" + settings.Password);}
                await Command(server, socket, proxyArgs, ct);
                Directory.CreateDirectory(request.OutputDirectory);
                await SaveAsync(receiptFile, new(identity, "running"), ct);
                await SaveAsync(Path.Combine(Root, "uncertain-server.json"), new(identity, "running"), ct);
                getStarted = true;
                await progress(new(null, Stage: "Downloading"));
                using (var download = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    using var quota = MegaCmdQuotaNotice.Observe(Path.Combine(NativeProfile, "megacmdserver.log"));
                    var get = Command(server, socket, ["get", publicLink, request.OutputDirectory], download.Token);
                    try
                    {
                        while (!get.IsCompleted)
                        {
                            await Task.WhenAny(get, Task.Delay(TimeSpan.FromSeconds(2), download.Token));
                            download.Token.ThrowIfCancellationRequested();
                            if (get.IsCompleted) break;
                            using var sampleTimeout = CancellationTokenSource.CreateLinkedTokenSource(download.Token);
                            sampleTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                            try
                            {
                                var sample = await Command(server, socket,
                                    ["transfers", "--only-downloads", "--show-completed", "--limit=1000", "--col-separator=|",
                                        "--output-cols=TAG,DESTINYPATH,PROGRESS,STATE"], sampleTimeout.Token);
                                if (ReadTransferSnapshot(sample, request.OutputDirectory) is { } snapshot)
                                    await progress(quota?.Apply(snapshot) ?? snapshot);
                            }
                            catch (CloudProviderException) { /* A progress snapshot cannot fail an otherwise valid transfer. */ }
                            catch (OperationCanceledException) when (!download.IsCancellationRequested) { }
                        }
                        await get;
                    }
                    finally
                    {
                        download.Cancel();
                        // A progress callback/parse failure must not leave the original client
                        // using the server while shutdown is trying to cancel its transfers.
                        try { await get; } catch (Exception) when (download.IsCancellationRequested) { }
                    }
                }
                safeExit = true;
                await progress(new(null, Stage: "Verifying"));
                var files = await CaptureAsync(request.OutputDirectory, ct);
                if (files.Count == 0) throw new CloudProviderException("MEGA_EMPTY_RESULT");
                await SaveAsync(receiptFile, new(identity, "completed", files), ct);
                await progress(new(100, files.Sum(f => f.Size), files.Sum(f => f.Size), "Completed"));
                return new(files.Select(f => Path.Combine(request.OutputDirectory, f.Path)).ToList());
            }
            finally
            {
                // Stopping a client does not stop MEGAcmd. Cancel only this exclusively owned
                // server's downloads before terminating it; retain any partial files and receipt.
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                if (!server.HasExited)
                {
                    try
                    {
                        if (getStarted && !safeExit) await Command(server, socket, ["transfers", "-c", "-a", "--only-downloads"], stop.Token);
                        await Command(server, socket, ["exit"], stop.Token);
                        safeExit = true;
                    }
                    catch (Exception) { /* Uncertain server state is fenced for manual review. */ }
                }
                Kill(server);
                await server.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(serverOut, serverError);
                if (safeExit) File.Delete(Path.Combine(Root, "uncertain-server.json"));
            }
        }
        finally {_gate.Release();}
    }

    private ProcessStartInfo Start(string executable, string socket, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(executable) {UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.Combine(Root, "process")};
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("MEGA", StringComparison.OrdinalIgnoreCase) ||
                     k.EndsWith("_PROXY", StringComparison.OrdinalIgnoreCase) || k is "LD_PRELOAD" or "LD_LIBRARY_PATH").ToArray()) info.Environment.Remove(key);
        info.Environment["MEGACMD_SOCKET_NAME"] = socket;
        info.Environment["LD_LIBRARY_PATH"] = dependency.LibraryDirectory;
        // Managed server has a different filename; neither PATH nor sibling lookup can spawn
        // a detached fallback if the explicitly owned process dies between commands.
        info.Environment["PATH"] = Path.Combine(Root, "process");
        info.Environment["LC_ALL"] = "C.UTF-8";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    private async Task<string> Command(Process server, string socket, IEnumerable<string> args, CancellationToken ct)
    {
        if (server.HasExited) throw new CloudProviderException("MEGA_SERVER_UNAVAILABLE");
        using var child = new Process {StartInfo = Start(dependency.Executable, socket, args)};
        if (!child.Start()) throw new CloudProviderException("MEGA_SERVER_UNAVAILABLE");
        using var cancel = ct.Register(() => Kill(child));
        var output = Tail(child.StandardOutput); var error = Tail(child.StandardError);
        try
        {
            child.StandardInput.Close();
            await child.WaitForExitAsync(ct);
            var text = await output + "\n" + await error;
            ct.ThrowIfCancellationRequested();
            if (child.ExitCode != 0) throw new CloudProviderException(Classify(text, args.FirstOrDefault() == "get" ? child.ExitCode : null));
            return text;
        }
        finally {Kill(child); await child.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(output, error);}
    }

    internal static string Classify(string text, int? downloadExitCode = null)
    {
        var value = text.ToLowerInvariant();
        // MEGA SDK codes are propagated (absolute value) by the get client. Do not turn an
        // unrelated loader/local-file "not found" message into proof that a share expired.
        if (downloadExitCode is 8 or 9) return "MEGA_SHARE_UNAVAILABLE";
        if (downloadExitCode is 14) return "MEGA_DECRYPTION_FAILED";
        if (downloadExitCode is 17 or 24) return "MEGA_TRANSFER_QUOTA";
        if (downloadExitCode is 11) return "MEGA_ACCESS_REQUIRED";
        if (value.Contains("overquota") || value.Contains("quota") || value.Contains("bandwidth limit")) return "MEGA_TRANSFER_QUOTA";
        if (value.Contains("decryption") || value.Contains("key is invalid")) return "MEGA_DECRYPTION_FAILED";
        if (value.Contains("public link has expired") || value.Contains("share has expired") || value.Contains("no longer available")) return "MEGA_SHARE_UNAVAILABLE";
        if (value.Contains("access denied") || value.Contains("access is denied")) return "MEGA_ACCESS_REQUIRED";
        if (value.Contains("connect") || value.Contains("timeout") || value.Contains("network")) return "MEGA_NETWORK_ERROR";
        return "MEGA_DOWNLOAD_FAILED";
    }
    private static void Kill(Process process) {try {if (!process.HasExited) process.Kill(true);} catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) {}}
    private static async Task<string> Tail(StreamReader reader)
    {
        var text = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer)) != 0) {text.Append(buffer, 0, count); if (text.Length > 65536) text.Remove(0, text.Length - 65536);}
        return text.ToString();
    }
    internal static void RequireEmptyOutput(string root)
    {
        if (!Path.IsPathFullyQualified(root) || HasProtocolCharacters(root)) throw new CloudProviderException("MEGA_OUTPUT_INVALID");
        if (File.Exists(root) || Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new CloudProviderException("MEGA_DOWNLOAD_REVIEW_REQUIRED");
        for (var parent = new DirectoryInfo(root); parent != null; parent = parent.Parent)
            if (parent.Exists && parent.LinkTarget != null) throw new CloudProviderException("MEGA_OUTPUT_INVALID");
    }
    private static async Task<IReadOnlyList<LocalFile>> CaptureAsync(string root, CancellationToken ct)
    {
        for (var parent = new DirectoryInfo(root); parent != null; parent = parent.Parent)
            if (parent.LinkTarget != null) throw new CloudProviderException("MEGA_OUTPUT_INVALID");
        var files = new List<LocalFile>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            ct.ThrowIfCancellationRequested();
            var attr = File.GetAttributes(entry);
            if ((attr & FileAttributes.ReparsePoint) != 0) throw new CloudProviderException("MEGA_OUTPUT_INVALID");
            if ((attr & FileAttributes.Directory) != 0) {pending.Push(entry); continue;}
            if (files.Count >= 100000) throw new CloudProviderException("MEGA_OUTPUT_INVALID");
            var file = await CloudPayloadHash.ComputeFileAsync(entry, ct);
            files.Add(new(Path.GetRelativePath(root, entry), file.Length, file.Sha256));
        }
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
    }
    private static async Task<bool> VerifyAsync(string root, IReadOnlyList<LocalFile> expected, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return false;
        return (await CaptureAsync(root, ct)).SequenceEqual(expected);
    }
    private static async Task SaveAsync(string path, Receipt receipt, CancellationToken ct)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try {await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(receipt), ct); File.Move(temp, path, true);}
        finally {File.Delete(temp);}
    }

    private CloudToolProxy.Settings? ResolveProxy() => CloudToolProxy.Resolve(
        proxy.ForDownloadTool(DownloadToolProxyTargets.MegaCmd, ThirdPartyId.Mega), new Uri("https://g.api.mega.co.nz"));

    internal static void ValidateProxy(CloudToolProxy.Settings? settings)
    {
        if (settings == null) return;
        if (!Uri.TryCreate(settings.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            HasProtocolCharacters(settings.Url) || HasProtocolCharacters(settings.Username ?? "") || HasProtocolCharacters(settings.Password ?? ""))
            throw new CloudProviderException("MEGA_PROXY_UNSUPPORTED");
    }

    // MEGAcmd serializes argv into its own textual command protocol, and does not escape
    // embedded quotation marks/backslashes. ProcessStartInfo alone cannot make these safe.
    private static bool HasProtocolCharacters(string value) => value.Any(c => char.IsControl(c) || c is '\'' or '"' or '\\');

    internal static CloudDownloadProgress? ReadTransferSnapshot(string output, string outputDirectory)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = Array.FindIndex(lines, line => line.Replace(" ", "") == "TAG|DESTINYPATH|PROGRESS|STATE");
        if (header < 0 || output.Contains("Showing first", StringComparison.Ordinal)) return null;
        double total = 0, completed = 0;
        var hasAllSizes = true;
        var tags = new HashSet<long>();
        var states = new HashSet<string>(StringComparer.Ordinal);
        var root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var line in lines.Skip(header + 1))
        {
            var cols = line.Split('|', StringSplitOptions.TrimEntries);
            if (cols.Length != 4 || !long.TryParse(cols[0], out var tag) || !Path.IsPathFullyQualified(cols[1])) return null;
            if (!Path.GetFullPath(cols[1]).StartsWith(root, StringComparison.Ordinal)) continue;
            if (!tags.Add(tag)) return null;
            states.Add(cols[3]);
            var match = Regex.Match(cols[2], @"\A([0-9]+(?:\.[0-9]+)?)%\s+of\s+([0-9]+(?:\.[0-9]+)?)\s*(B|KB|MB|GB|TB)\z");
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent) ||
                percent is < 0 or > 100 || !double.TryParse(match.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size))
            {
                hasAllSizes = false;
                continue;
            }
            var exponent = match.Groups[3].Value switch {"KB" => 1, "MB" => 2, "GB" => 3, "TB" => 4, _ => 0};
            var weight = size * Math.Pow(1024, exponent);
            total += weight; completed += weight * percent / 100;
        }
        if (tags.Count == 0) return null;
        // Completed siblings should not hide a paused or retrying transfer. Final completion is
        // reported separately after get exits and the local manifest is verified.
        states.Remove("COMPLETED");
        var stage = states.Count switch
        {
            0 => "TransferFinishing",
            _ when states.Contains("ACTIVE") => "Downloading",
            1 => states.Single() switch
            {
                "QUEUED" => "Queued", "RETRYING" => "TransferRetrying", "PAUSED" => "TransferPaused",
                "COMPLETING" => "TransferFinishing", "CANCELLED" => "TransferCancelled", "FAILED" => "TransferFailed",
                _ => "TransferStateUnavailable"
            },
            _ => "TransferMixed"
        };
        // The CLI exposes rounded sizes, so emit progress only, never invent exact byte counts.
        double? percentage = hasAllSizes && total > 0 && double.IsFinite(total)
            ? Math.Clamp(completed / total * 100, 0, 99.9) : null;
        return new(percentage, Stage: stage);
    }
}
