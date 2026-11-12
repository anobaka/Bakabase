using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.OAuth;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

/// <summary>
/// A controlled account-remote engine, deliberately separate from public share resolution and
/// the workflow artifact ledger. Callers authorize destination storage and register the exact
/// returned files in that ledger. No arbitrary backend, config, CLI arguments or shell commands.
/// </summary>
public sealed class RcloneRemoteEngine
{
    private readonly Func<CancellationToken, Task<string>> _executable;
    private readonly string _root;
    private readonly BakabaseWebProxy _proxy;
    private readonly FileProcessingWorkspaceResolver _locations;
    private readonly Func<string?> _workspaceRoot;
    private readonly IUserStoragePolicy? _storage;
    private readonly IStorageWriteGuard? _writes;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accounts = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RcloneRemoteEngine(AppService app, RcloneService dependency, BakabaseWebProxy proxy,
        IUserStoragePolicy storage, IStorageWriteGuard? writes = null)
        : this(Path.Combine(app.AppDataDirectory, "cloud-downloads", "rclone"), async ct =>
        { await dependency.EnsureReadyAsync(ct); return dependency.Executable; }, proxy,
            new(app.AppDataDirectory, storage), storage: storage, writes: writes) { }

    internal RcloneRemoteEngine(string privateDirectory, Func<CancellationToken, Task<string>> executable,
        BakabaseWebProxy proxy, FileProcessingWorkspaceResolver? locations = null,
        Func<string?>? workspaceRoot = null, IUserStoragePolicy? storage = null, IStorageWriteGuard? writes = null)
    {
        _root = Path.GetFullPath(privateDirectory); _executable = executable; _proxy = proxy;
        _locations = locations ?? new(_root); _workspaceRoot = workspaceRoot ?? (() => null); _storage = storage; _writes = writes;
    }

    internal string IdentityPath(string path) => _locations.IdentityPath(Path.GetFullPath(path));

    /// <summary>Local credential configuration only. Online ListAsync verifies the account. No credentials are returned.</summary>
    public async Task ConfigurePikPakAsync(string accountId, string username, string password, CancellationToken ct)
    {
        CheckCredential(username); CheckCredential(password);
        await Locked(new("pikpak", accountId), async directory =>
        {
            var executable = await _executable(ct);
            var obscured = (await RcloneProcess.RunAsync(Start(executable, directory, ["obscure", "-"], null), password, null, ct)).Trim();
            if (obscured.Length is < 1 or > 8192 || obscured.Any(char.IsControl))
                throw new RcloneRemoteException("RCLONE_CREDENTIAL_INVALID");
            // rclone obscure is reversible, not encryption. The profile remains private and is
            // never exposed by a DTO, process argument, exception or log.
            // Advance identity BEFORE replacing credentials. A crash can invalidate a cache,
            // but cannot make a different account reuse a prior account's completed receipt.
            await WritePrivateAsync(Path.Combine(directory, "generation"), Guid.NewGuid().ToString("N"), ct);
            await WritePrivateAsync(Path.Combine(directory, "rclone.conf"),
                $"[managed]\ntype = pikpak\nuser = {username}\npass = {obscured}\ndevice_id = {Guid.NewGuid():N}\nno_media_link = true\n", ct);
            await WritePrivateAsync(Path.Combine(directory, "profile.json"), JsonSerializer.Serialize(new ProfileStamp("pikpak", null), Json), ct);
            return true;
        }, ct, create: true);
    }

    public async Task ConfigureOneDriveAsync(string accountId, RcloneOneDriveConfiguration configuration, CancellationToken ct)
    {
        var token = ValidateOneDrive(configuration);
        await Locked(new("oneDrive", accountId), async directory =>
        {
            var config = $"[managed]\ntype = onedrive\ntoken = {token}\ndrive_id = {configuration.DriveId}\n" +
                $"drive_type = {configuration.DriveType}\naccess_scopes = Files.Read Files.Read.All Sites.Read.All offline_access\n";
            if (!string.IsNullOrEmpty(configuration.ClientId)) config += "client_id = " + configuration.ClientId + "\n";
            // oauthutil.OverrideCredentials consumes this literal value. Unlike PikPak's pass,
            // client_secret is Sensitive but is not an IsPassword/obscured rclone option.
            if (!string.IsNullOrEmpty(configuration.ClientSecret)) config += "client_secret = " + configuration.ClientSecret + "\n";
            if (!string.IsNullOrEmpty(configuration.AuthUrl)) config += "auth_url = " + configuration.AuthUrl + "\n";
            if (!string.IsNullOrEmpty(configuration.TokenUrl)) config += "token_url = " + configuration.TokenUrl + "\n";
            if (!string.IsNullOrEmpty(configuration.RootFolderId)) config += "root_folder_id = " + configuration.RootFolderId + "\n";
            await Configure(directory, config, new("oneDrive", null), ct);
            return true;
        }, ct, create: true);
    }

    public async Task ConfigurePixeldrainAsync(string accountId, string rootFolderId, string? apiKey, CancellationToken ct)
    {
        CheckRootFolderId(rootFolderId);
        if (!string.IsNullOrEmpty(apiKey)) CheckCredential(apiKey);
        if (rootFolderId == "me" && string.IsNullOrEmpty(apiKey)) throw new RcloneRemoteException("RCLONE_PIXELDRAIN_API_KEY_REQUIRED");
        await Locked(new("pixeldrain", accountId), async directory =>
        {
            await Configure(directory, PixeldrainConfiguration(rootFolderId, apiKey), new("pixeldrain", null), ct);
            return true;
        }, ct, create: true);
    }

    /// <summary>Idempotent anonymous public-directory profile. No API key or personal 'me' filesystem.</summary>
    public async Task<RcloneProfileRef> EnsurePixeldrainPublicProfileAsync(string directoryId, CancellationToken ct)
    {
        CheckRootFolderId(directoryId);
        if (directoryId == "me") throw new RcloneRemoteException("RCLONE_PIXELDRAIN_PUBLIC_DIRECTORY_REQUIRED");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("pixeldrain.public.v1\n" + directoryId)))[..32].ToLowerInvariant();
        var profile = new RcloneProfileRef("pixeldrain", id);
        await Locked(profile, async directory =>
        {
            if (await HasProfileCore(directory, profile.ProviderId, ct))
            {
                var stamp = JsonSerializer.Deserialize<ProfileStamp>(await File.ReadAllTextAsync(Path.Combine(directory, "profile.json"), ct), Json)!;
                if (stamp.PublicDirectoryId != directoryId) throw new RcloneRemoteException("RCLONE_PROFILE_MISMATCH");
                return true;
            }
            await Configure(directory, PixeldrainConfiguration(directoryId, null), new("pixeldrain", directoryId), ct);
            return true;
        }, ct, create: true);
        return profile;
    }

    public Task<bool> HasProfileAsync(RcloneProfileRef profile, CancellationToken ct) => Locked(profile,
        directory => HasProfileCore(directory, profile.ProviderId, ct), ct);

    public Task<string?> GetProfileVersionAsync(RcloneProfileRef profile, CancellationToken ct) => Locked<string?>(profile,
        async directory => await HasProfileCore(directory, profile.ProviderId, ct)
            ? await File.ReadAllTextAsync(Path.Combine(directory, "generation"), ct) : null, ct);

    /// <summary>Logout removes only authorization files; verified copies and user outputs remain intact.</summary>
    public Task RemoveProfileAsync(RcloneProfileRef profile, CancellationToken ct) => Locked(profile, directory =>
    {
        if (!Directory.Exists(directory)) return Task.FromResult(true);
        foreach (var file in Directory.EnumerateFiles(directory).Where(f => Path.GetFileName(f).StartsWith("rclone.conf", StringComparison.Ordinal) ||
                     Path.GetFileName(f) is "generation" or "profile.json"))
        { ct.ThrowIfCancellationRequested(); RejectLinks(file); File.Delete(file); }
        return Task.FromResult(true);
    }, ct);

    public Task<IReadOnlyList<RcloneRemoteFile>> ListAsync(string accountId, string remotePath,
        ThirdPartyId? sourceSite, CancellationToken ct) => ListAsync(LegacyProfile(accountId, sourceSite), remotePath, ct);

    public Task<IReadOnlyList<RcloneRemoteFile>> ListAsync(RcloneProfileRef profile, string remotePath,
        CancellationToken ct, string? expectedProfileVersion = null) => Locked(profile, async directory =>
    {
        if (!await HasProfileCore(directory, profile.ProviderId, ct)) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        await RequireGeneration(directory, expectedProfileVersion, ct);
        return await ListCore(await _executable(ct), directory, remotePath, Site(profile.ProviderId), ct);
    }, ct);

    /// <summary>
    /// Copies the exact previously listed file snapshot into owned staging, checks remote identity
    /// and size/hash where available, then publishes without overwriting a different destination.
    /// Cancelled partial files are never reported as complete. Byte-range restart is not promised.
    /// </summary>
    public Task<RcloneCopyResult> CopyAsync(string accountId, string remotePath, string destinationDirectory,
        IReadOnlyList<RcloneRemoteFile> expectedManifest, Action<RcloneCopyProgress>? progress,
        ThirdPartyId? sourceSite, CancellationToken ct) => CopyAsync(LegacyProfile(accountId, sourceSite), remotePath,
            destinationDirectory, expectedManifest, progress, ct);

    public Task<RcloneCopyResult> CopyAsync(RcloneProfileRef profile, string remotePath, string destinationDirectory,
        IReadOnlyList<RcloneRemoteFile> expectedManifest, Action<RcloneCopyProgress>? progress,
        CancellationToken ct, string? expectedProfileVersion = null, string? workspaceDirectory = null) => Locked(profile, async directory =>
    {
        if (!await HasProfileCore(directory, profile.ProviderId, ct)) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        var generation = await RequireGeneration(directory, expectedProfileVersion, ct);
        var sourceSite = Site(profile.ProviderId);
        var expected = ValidateManifest(expectedManifest);
        if (!Path.IsPathFullyQualified(destinationDirectory)) throw new RcloneRemoteException("RCLONE_PATH_INVALID");
        var destination = Path.GetFullPath(destinationDirectory); RejectLinks(destination);
        _storage?.EnsurePathAllowed(destination);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { generation, remotePath, destination = _locations.IdentityPath(destination), expected }, Json)))).ToLowerInvariant();
        var operation = Path.Combine(directory, "copies", identity); PrivateDirectory(operation);
        var receiptPath = Path.Combine(operation, "completed.json");
        List<RcloneLocalFile>? receipt = null;
        if (File.Exists(receiptPath))
        {
            RejectLinks(receiptPath);
            receipt = JsonSerializer.Deserialize<List<RcloneLocalFile>>(await File.ReadAllTextAsync(receiptPath, ct), Json);
            if (receipt == null || !ReceiptMatchesManifest(expected, receipt))
                throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
            // The original remote snapshot and account generation are pinned. A verified delivery
            // is enough to satisfy the same copy even after its staging files were reclaimed.
            if (await VerifyDestination(destination, receipt, ct))
            {
                await ReclaimStage(operation, destination, expected, receipt, ct);
                return new(receipt.Select(f => f with { Path = Within(destination, f.Path) }).ToArray(), true);
            }
        }
        var reused = receipt != null;
        string stage;
        if (receipt != null)
        {
            stage = await ResolveStage(profile, operation, identity, destination, workspaceDirectory, ct, create: false);
            // A recorded completed copy must never silently download again merely because a
            // destination was moved or a staging volume is disconnected.
            if (!await VerifyLocal(stage, expected, receipt, ct))
                throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
        }
        else
        {
            var executable = await _executable(ct);
            var current = await ListCore(executable, directory, remotePath, sourceSite, ct);
            if (!SameManifest(expected, current)) throw new RcloneRemoteException("RCLONE_REMOTE_CHANGED");
            stage = await ResolveStage(profile, operation, identity, destination, workspaceDirectory, ct);
            // A failed copy cannot prove completed files when the provider has no hash. Restart
            // in a fresh stage. Preserve the previous attempt rather than allowing unknown
            // partial files to poison all retries or deleting evidence after a crash.
            if (Directory.EnumerateFileSystemEntries(stage).Any())
            {
                RejectLinks(stage);
                Directory.Move(stage, Path.Combine(Path.GetDirectoryName(stage)!, "incomplete-" + Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(stage);
            }
            var list = Path.Combine(operation, "files.txt");
            await WritePrivateAsync(list, string.Join('\n', expected.Select(f => f.Path)) + "\n", ct);
            var args = new List<string> { "copy", Remote(remotePath), stage,
                "--files-from-raw", list, "--ignore-times", "--checkers", "2", "--transfers", "2",
                "--use-json-log", "--stats", "1s", "--log-level", "INFO" };
            if (expected.Count != 0)
            {
                // A restored locator may still name another volume than the caller's current
                // preference. Guard the actual pinned stage and native-tool temporary directory.
                Task<string> Copy(CancellationToken token) => RcloneProcess.RunAsync(
                    Start(executable, directory, args, sourceSite, Path.GetDirectoryName(stage)), null, progress, token);
                if (_writes == null) await Copy(ct);
                else await _writes.RunAsync([stage, Path.GetDirectoryName(stage)!], StorageWriteScope.Current, Copy, ct);
            }
            var after = await ListCore(executable, directory, remotePath, sourceSite, ct);
            if (!SameManifest(expected, after)) throw new RcloneRemoteException("RCLONE_REMOTE_CHANGED");
            receipt = await CaptureLocal(stage, expected, ct);
            await WritePrivateAsync(receiptPath, JsonSerializer.Serialize(receipt, Json), ct);
        }
        Directory.CreateDirectory(destination); RejectLinks(destination);
        foreach (var file in receipt)
        {
            ct.ThrowIfCancellationRequested();
            var target = Within(destination, file.Path); var source = Within(stage, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); RejectLinks(target); RejectLinks(source);
            if (File.Exists(target))
            {
                if (!await Matches(target, file, ct)) throw new RcloneRemoteException("RCLONE_DESTINATION_CONFLICT");
                continue;
            }
            // Copy to a unique sibling and atomically publish; managed staging remains usable
            // by other consumers. Unknown files and a different existing destination are kept.
            var temporary = target + ".rclone-" + Guid.NewGuid().ToString("N");
            try
            {
                if (_writes != null) await _writes.CheckAsync(temporary, StorageWriteScope.Current, ct);
                await using (var input = File.OpenRead(source))
                await using (var output = new FileStream(temporary, FileMode.CreateNew))
                    if (_writes == null) await input.CopyToAsync(output, ct);
                    else await _writes.CopyAsync(input, output, temporary, StorageWriteScope.Current, ct);
                if (!await Matches(temporary, file, ct)) throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
                RejectLinks(target); File.Move(temporary, target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (var file in receipt)
            if (!await Matches(Within(destination, file.Path), file, ct)) throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
        await ReclaimStage(operation, destination, expected, receipt, ct);
        return new RcloneCopyResult(receipt.Select(f => f with { Path = Within(destination, f.Path) }).ToArray(), reused);
    }, ct);

    private sealed record CopyWorkspace(int Version, string Identity, string Directory);

    private async Task<string> ResolveStage(RcloneProfileRef profile, string operation, string identity,
        string destination, string? workspaceDirectory, CancellationToken ct, bool create = true)
    {
        var locator = Path.Combine(operation, "workspace.json");
        var found = await ReadStage(operation, identity, ct);
        if (found != null)
        {
            // Missing pinned storage must not fall back to a different disk or a fresh transfer.
            if (!Directory.Exists(found)) throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
            RejectLinks(found);
            return found;
        }
        if (!create) throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
        // A new download directory may not exist yet. Its existing immediate parent is the
        // boundary: never climb to /Volumes or / and silently stage on another volume.
        var basis = Directory.Exists(destination) ? destination : Path.GetDirectoryName(destination);
        if (basis == null) throw new RcloneRemoteException("RCLONE_PATH_INVALID");
        var workspaceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "rclone-copy-v1\n" + profile.ProviderId + "\n" + profile.Id + "\n" + identity))).ToLowerInvariant();
        var workspace = await _locations.ResolveAsync("rclone-copies", workspaceKey, 1, basis, workspaceDirectory ?? _workspaceRoot(), ct);
        var stage = Path.Combine(workspace, "files"); RejectLinks(stage);
        Directory.CreateDirectory(stage);
        await WritePrivateAsync(locator, JsonSerializer.Serialize(new CopyWorkspace(1, identity, stage), Json), ct);
        return stage;
    }

    private async Task<string?> ReadStage(string operation, string identity, CancellationToken ct)
    {
        var locator = Path.Combine(operation, "workspace.json"); RejectLinks(locator);
        if (File.Exists(locator))
        {
            var saved = JsonSerializer.Deserialize<CopyWorkspace>(await File.ReadAllTextAsync(locator, ct), Json);
            if (saved is not {Version: 1} || saved.Identity != identity || !Path.IsPathFullyQualified(saved.Directory))
                throw new RcloneRemoteException("RCLONE_LOCAL_CHANGED");
            return _locations.Relocate(saved.Directory);
        }
        var legacy = _locations.Relocate(Path.Combine(operation, "files")); RejectLinks(legacy);
        return Directory.Exists(legacy) ? legacy : null;
    }

    private async Task ReclaimStage(string operation, string destination, IReadOnlyList<RcloneRemoteFile> expected,
        IReadOnlyList<RcloneLocalFile> receipt, CancellationToken ct)
    {
        try
        {
            await WritePrivateAsync(Path.Combine(operation, "published.json"),
                JsonSerializer.Serialize(new {destination, files = receipt, verifiedAt = DateTime.UtcNow}, Json), ct);
            var stage = await ReadStage(operation, Path.GetFileName(operation), ct);
            if (stage == null || !Directory.Exists(stage) || !await VerifyLocal(stage, expected, receipt, ct)) return;
            // Only this exact, verified receipt is disposable. Unknown files or failed attempts
            // are retained; no directory-wide deletion or age-based sweep is used.
            foreach (var file in receipt)
            {
                ct.ThrowIfCancellationRequested();
                var path = Within(stage, file.Path); RejectLinks(path); File.Delete(path);
            }
            FileProcessingPlanExecutor.RemoveEmptyDirectories(stage);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or
            RcloneRemoteException or JsonException or InvalidOperationException)
        { /* Successful delivery remains successful if best-effort staging reclamation cannot finish. */ }
    }

    private static bool ReceiptMatchesManifest(IReadOnlyList<RcloneRemoteFile> expected, IReadOnlyList<RcloneLocalFile> receipt) =>
        receipt.Count == expected.Count && receipt.Select(f => f.Path).SequenceEqual(expected.Select(f => f.Path)) &&
        receipt.Zip(expected).All(p => p.First.Size == p.Second.Size && p.First.Sha256.Length == 64 && p.First.Sha256.All(Uri.IsHexDigit));

    private static async Task<bool> VerifyDestination(string destination, IReadOnlyList<RcloneLocalFile> receipt, CancellationToken ct)
    {
        foreach (var file in receipt)
            if (!await Matches(Within(destination, file.Path), file, ct)) return false;
        return true;
    }

    private async Task<IReadOnlyList<RcloneRemoteFile>> ListCore(string executable, string directory, string remotePath,
        ThirdPartyId? site, CancellationToken ct)
    {
        if (!File.Exists(Path.Combine(directory, "rclone.conf"))) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        var json = await RcloneProcess.RunAsync(Start(executable, directory,
            ["lsjson", Remote(remotePath), "--recursive", "--files-only", "--hash"], site), null, null, ct);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException();
            var files = new List<RcloneRemoteFile>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (files.Count >= 100000) throw new RcloneRemoteException("RCLONE_MANIFEST_LIMIT");
                if (entry.TryGetProperty("IsDir", out var dir) && dir.GetBoolean()) continue;
                var hashes = entry.TryGetProperty("Hashes", out var h) && h.ValueKind == JsonValueKind.Object
                    ? h.EnumerateObject().Where(v => v.Value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(v.Value.GetString()))
                        .ToDictionary(v => v.Name.ToLowerInvariant(), v => v.Value.GetString()!) : null;
                files.Add(new(entry.GetProperty("Path").GetString()!, entry.GetProperty("Size").GetInt64(),
                    entry.TryGetProperty("ID", out var id) ? id.GetString() : null, hashes,
                    entry.TryGetProperty("ModTime", out var modified) ? modified.GetString() : null));
            }
            return ValidateManifest(files);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID"); }
    }

    private async Task<T> Locked<T>(RcloneProfileRef profile, Func<string, Task<T>> action, CancellationToken ct, bool create = false)
    {
        _ = Site(profile.ProviderId);
        var id = profile.Id;
        if (!Guid.TryParseExact(id, "N", out _)) throw new RcloneRemoteException("RCLONE_ACCOUNT_INVALID");
        id = id.ToLowerInvariant();
        var gate = _accounts.GetOrAdd(profile.ProviderId + ":" + id, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var provider = Path.Combine(_root, profile.ProviderId); var directory = Path.Combine(provider, id);
            RejectLinks(directory);
            if (create) { PrivateDirectory(_root); PrivateDirectory(provider); PrivateDirectory(directory); }
            return await action(directory);
        }
        finally { gate.Release(); }
    }

    internal ProcessStartInfo Start(string executable, string directory, IEnumerable<string> arguments, ThirdPartyId? site,
        string? payloadWorkspace = null)
    {
        if (payloadWorkspace != null)
        {
            _storage?.EnsurePathAllowed(payloadWorkspace);
            RejectLinks(payloadWorkspace);
            if (!Directory.Exists(payloadWorkspace))
                throw new DirectoryNotFoundException("The download workspace is unavailable. Reconnect its storage before continuing.");
        }
        var cache = Path.Combine(payloadWorkspace ?? directory, "cache");
        var temporary = Path.Combine(payloadWorkspace ?? directory, "temporary");
        // rclone requires an existing --temp-dir. Keep copy-time payload caches on the same
        // selected storage, while configuration/listing credentials remain in the private profile.
        foreach (var path in new[] {cache, temporary})
        {
            RejectLinks(path);
            if (payloadWorkspace == null) PrivateDirectory(path);
            else Directory.CreateDirectory(path);
        }
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RCLONE_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        foreach (var arg in new[] { "--config", Path.Combine(directory, "rclone.conf"), "--cache-dir", cache,
                     "--temp-dir", temporary, "--ask-password=false", "--retries", "1", "--low-level-retries", "2" })
            start.ArgumentList.Add(arg);
        RejectLinks(Path.Combine(directory, "rclone.conf"));
        CloudToolProxy.ApplyToEnvironment(start, _proxy.ForDownloadTool(DownloadToolProxyTargets.Rclone, site),
            site switch { ThirdPartyId.OneDrive => new Uri("https://graph.microsoft.com"),
                ThirdPartyId.Pixeldrain => new Uri("https://pixeldrain.com/api"),
                null or ThirdPartyId.PikPak => new Uri("https://api-drive.mypikpak.com"),
                _ => throw new RcloneRemoteException("RCLONE_PROVIDER_UNSUPPORTED") });
        return start;
    }

    private sealed record ProfileStamp(string ProviderId, string? PublicDirectoryId);
    private static ThirdPartyId Site(string provider) => provider switch
    { "pikpak" => ThirdPartyId.PikPak, "oneDrive" => ThirdPartyId.OneDrive, "pixeldrain" => ThirdPartyId.Pixeldrain,
        _ => throw new RcloneRemoteException("RCLONE_PROVIDER_UNSUPPORTED") };
    private static RcloneProfileRef LegacyProfile(string id, ThirdPartyId? site) => new(site switch
    { null or ThirdPartyId.PikPak => "pikpak", ThirdPartyId.OneDrive => "oneDrive", ThirdPartyId.Pixeldrain => "pixeldrain",
        _ => throw new RcloneRemoteException("RCLONE_PROVIDER_UNSUPPORTED") }, id);

    private static async Task<bool> HasProfileCore(string directory, string provider, CancellationToken ct)
    {
        foreach (var name in new[] { "rclone.conf", "generation", "profile.json" })
        { var file = Path.Combine(directory, name); RejectLinks(file); if (!File.Exists(file)) return false; }
        try
        {
            var stamp = JsonSerializer.Deserialize<ProfileStamp>(await File.ReadAllTextAsync(Path.Combine(directory, "profile.json"), ct), Json);
            return stamp?.ProviderId == provider && Guid.TryParseExact(await File.ReadAllTextAsync(Path.Combine(directory, "generation"), ct), "N", out _);
        }
        catch (JsonException) { return false; }
    }
    private static async Task Configure(string directory, string config, ProfileStamp stamp, CancellationToken ct)
    {
        await WritePrivateAsync(Path.Combine(directory, "generation"), Guid.NewGuid().ToString("N"), ct);
        await WritePrivateAsync(Path.Combine(directory, "rclone.conf"), config, ct);
        await WritePrivateAsync(Path.Combine(directory, "profile.json"), JsonSerializer.Serialize(stamp, Json), ct);
    }
    private static async Task<string> RequireGeneration(string directory, string? expected, CancellationToken ct)
    {
        var path = Path.Combine(directory, "generation"); RejectLinks(path);
        if (!File.Exists(path)) throw new RcloneRemoteException("RCLONE_ACCOUNT_NOT_CONFIGURED");
        var current = await File.ReadAllTextAsync(path, ct);
        if (!Guid.TryParseExact(current, "N", out _)) throw new RcloneRemoteException("RCLONE_ACCOUNT_INVALID");
        if (expected != null && !string.Equals(expected, current, StringComparison.Ordinal))
            throw new RcloneRemoteException("RCLONE_ACCOUNT_CHANGED");
        return current;
    }
    // v1.75.2 backend/pixeldrain/pixeldrain.go declares root_folder_id; directory_id
    // appears in an older interactive documentation example and is not the config key.
    private static string PixeldrainConfiguration(string root, string? key) =>
        "[managed]\ntype = pixeldrain\nroot_folder_id = " + root + "\n" + (string.IsNullOrEmpty(key) ? "" : "api_key = " + key + "\n");
    private static void CheckRootFolderId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]+\z"))
            throw new RcloneRemoteException("RCLONE_PIXELDRAIN_DIRECTORY_INVALID");
    }
    private static string ValidateOneDrive(RcloneOneDriveConfiguration configuration)
    {
        if (configuration.DriveType is not ("personal" or "business" or "documentLibrary") || string.IsNullOrEmpty(configuration.DriveId) ||
            !Regex.IsMatch(configuration.DriveId, @"\A[A-Za-z0-9!_.~-]{1,512}\z") ||
            !string.IsNullOrEmpty(configuration.RootFolderId) && !Regex.IsMatch(configuration.RootFolderId, @"\A[A-Za-z0-9!_.~-]{1,512}\z") ||
            !string.IsNullOrEmpty(configuration.ClientId) && !Guid.TryParseExact(configuration.ClientId, "D", out _))
            throw new RcloneRemoteException("RCLONE_ONEDRIVE_CONFIGURATION_INVALID");
        if (!string.IsNullOrEmpty(configuration.ClientSecret)) CheckCredential(configuration.ClientSecret);
        if (configuration.AuthUrl != null || configuration.TokenUrl != null)
        {
            try
            {
            var provider = new MicrosoftGraphOAuthProvider();
            if (!Uri.TryCreate(configuration.AuthUrl, UriKind.Absolute, out var auth) || auth.Host != "login.microsoftonline.com" ||
                auth.AbsolutePath.Split('/') is not {Length: 5} segments ||
                configuration.AuthUrl != provider.GetAuthorizationEndpoint(segments[1]).AbsoluteUri ||
                configuration.TokenUrl != provider.GetTokenEndpoint(segments[1]).AbsoluteUri)
                throw new RcloneRemoteException("RCLONE_ONEDRIVE_CONFIGURATION_INVALID");
            }
            catch (OAuthAuthorizationException) { throw new RcloneRemoteException("RCLONE_ONEDRIVE_CONFIGURATION_INVALID"); }
        }
        if (string.IsNullOrEmpty(configuration.TokenJson) || configuration.TokenJson.Length > 131072)
            throw new RcloneRemoteException("RCLONE_CREDENTIAL_INVALID");
        try
        {
            using var document = JsonDocument.Parse(configuration.TokenJson);
            var token = document.RootElement;
            var access = token.GetProperty("access_token").GetString();
            if (string.IsNullOrWhiteSpace(access) || token.ValueKind != JsonValueKind.Object) throw new JsonException();
            // Serialize the reviewed token shape as a single INI value; controls/newlines are escaped.
            return JsonSerializer.Serialize(new { access_token = access,
                token_type = token.TryGetProperty("token_type", out var type) ? type.GetString() : "Bearer",
                refresh_token = token.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                expiry = token.TryGetProperty("expiry", out var expiry) ? expiry.GetString() : null }, Json);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new RcloneRemoteException("RCLONE_ONEDRIVE_TOKEN_INVALID"); }
    }

    internal static IReadOnlyList<RcloneRemoteFile> ValidateManifest(IReadOnlyList<RcloneRemoteFile> files)
    {
        if (files.Count > 100000) throw new RcloneRemoteException("RCLONE_MANIFEST_LIMIT");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file == null) throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID");
            SafePath(file.Path, false);
            if (file.Size < 0 || !seen.Add(file.Path)) throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID");
        }
        foreach (var file in files)
        {
            var parts = file.Path.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (seen.Contains(string.Join('/', parts.Take(i)))) throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID");
        }
        return files.Select(file => file with { Hashes = NormalizeHashes(file.Hashes) })
            .OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyDictionary<string, string>? NormalizeHashes(IReadOnlyDictionary<string, string>? hashes)
    {
        if (hashes == null || hashes.Count == 0) return null;
        if (hashes.Count > 32) throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID");
        var normalized = new Dictionary<string, string>();
        foreach (var pair in hashes)
        {
            var key = pair.Key.Replace("-", "").ToLowerInvariant();
            if (string.IsNullOrEmpty(pair.Value)) continue;
            var value = key is "md5" or "sha256" ? pair.Value.ToLowerInvariant() : pair.Value;
            if (value.Length > 256 || key is "md5" && (value.Length != 32 || !value.All(Uri.IsHexDigit)) ||
                key is "sha256" && (value.Length != 64 || !value.All(Uri.IsHexDigit)) || !normalized.TryAdd(key, value))
                throw new RcloneRemoteException("RCLONE_MANIFEST_INVALID");
        }
        return normalized.Count == 0 ? null : normalized;
    }

    internal static string Remote(string path)
    {
        // A leading slash denotes the configured cloud root, never a local filesystem.
        // Remove exactly one: //, dot segments, drive prefixes and other unsafe paths still fail.
        if (path?.StartsWith('/') == true) path = path[1..];
        SafePath(path!, true); return "managed:" + path;
    }
    private static void SafePath(string path, bool allowEmpty)
    {
        if (path == null || path.Length > 4096 || path.Split('/').Length > 64 || (!allowEmpty && path.Length == 0) ||
            path.Any(c => char.IsControl(c) || c is '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*') ||
            path.Split('/').Any(p => p is "." or ".." || p.Length == 0 && path.Length != 0 ||
                p.EndsWith(' ') || p.EndsWith('.') || IsReserved(p)))
            throw new RcloneRemoteException("RCLONE_PATH_INVALID");
    }
    private static bool IsReserved(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "NUL" or "PRN" or "AUX" || name.Length == 4 &&
            (name.StartsWith("COM") || name.StartsWith("LPT")) && name[3] is >= '0' and <= '9';
    }
    private static bool SameManifest(IReadOnlyList<RcloneRemoteFile> a, IReadOnlyList<RcloneRemoteFile> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.Path == pair.Second.Path && pair.First.Size == pair.Second.Size &&
            pair.First.Id == pair.Second.Id && pair.First.ModifiedAt == pair.Second.ModifiedAt &&
            JsonSerializer.Serialize(pair.First.Hashes?.OrderBy(p => p.Key), Json) ==
            JsonSerializer.Serialize(pair.Second.Hashes?.OrderBy(p => p.Key), Json));

    private static async Task<List<RcloneLocalFile>> CaptureLocal(string root, IReadOnlyList<RcloneRemoteFile> expected, CancellationToken ct)
    {
        RejectLinks(root);
        var actual = EnumerateSafeFiles(root).ToArray();
        if (actual.Length != expected.Count) throw new RcloneRemoteException("RCLONE_LOCAL_MANIFEST_MISMATCH");
        var result = new List<RcloneLocalFile>();
        foreach (var file in expected)
        {
            var path = Within(root, file.Path); RejectLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size) throw new RcloneRemoteException("RCLONE_LOCAL_MANIFEST_MISMATCH");
            await using var stream = File.OpenRead(path);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (file.Hashes != null)
                foreach (var hash in file.Hashes)
                    if (hash.Key is "sha256" or "md5")
                    {
                        stream.Position = 0;
                        var value = hash.Key == "sha256" ? sha : Convert.ToHexString(await MD5.HashDataAsync(stream, ct));
                        if (!value.Equals(hash.Value, StringComparison.OrdinalIgnoreCase)) throw new RcloneRemoteException("RCLONE_HASH_MISMATCH");
                    }
            result.Add(new(file.Path, file.Size, sha));
        }
        return result;
    }
    private static async Task<bool> VerifyLocal(string root, IReadOnlyList<RcloneRemoteFile> expected,
        IReadOnlyList<RcloneLocalFile> receipt, CancellationToken ct)
    {
        if (!Directory.Exists(root) || !ReceiptMatchesManifest(expected, receipt)) return false;
        foreach (var file in receipt)
            if (!await Matches(Within(root, file.Path), file, ct)) return false;
        return EnumerateSafeFiles(root).Count() == receipt.Count;
    }
    private static async Task<bool> Matches(string path, RcloneLocalFile file, CancellationToken ct)
    {
        RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private static string Within(string root, string relative)
    {
        SafePath(relative, false); return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    private static void CheckCredential(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            throw new RcloneRemoteException("RCLONE_CREDENTIAL_INVALID");
    }
    internal static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (new FileInfo(current).LinkTarget != null ||
                (File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new RcloneRemoteException("RCLONE_LINK_PATH_REJECTED");
        }
    }
    private static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                RejectLinks(entry);
                if (Directory.Exists(entry)) directories.Push(entry);
                else yield return entry;
            }
        }
    }
    private static void PrivateDirectory(string directory)
    {
        RejectLinks(directory); Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, (UnixFileMode)0x1c0);
    }
    private static async Task WritePrivateAsync(string path, string content, CancellationToken ct)
    {
        RejectLinks(path); var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = (UnixFileMode)0x180;
            await using (var output = new FileStream(temporary, options))
            await using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                await writer.WriteAsync(content.AsMemory(), ct);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
