using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

/// <summary>Official share-download: no cloud save, quota-changing operation or account-file
/// protocol. A complete leaf manifest is required before any payload is requested.</summary>
public sealed class TeraBoxCloudProvider(TeraBoxAccountService accounts, IUserStoragePolicy? storage = null)
    : ICloudDownloadProvider, ICloudDownloadRecoveryProvider, ICloudDownloadAbandonmentProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operations = new(StringComparer.Ordinal);
    public string ProviderId => "terabox";
    public string MethodId => "terabox.cli";
    public bool CanCorrectAccessCodeAfterFailure(string code) => code == "TERABOX_ACCESS_CODE_REQUIRED";
    public bool CanRevalidateDownloadFailure(string code) => code is "TERABOX_ACCOUNT_REQUIRED" or "TERABOX_ACCOUNT_CHANGED" or
        "TERABOX_ACCESS_CODE_REQUIRED" or "TERABOX_PERMISSION_REQUIRED" or "TERABOX_NETWORK_UNAVAILABLE" or
        "TERABOX_SIGNATURE_EXPIRED" or "TERABOX_SHARE_UNAVAILABLE" or "TERABOX_CLI_REQUEST_FAILED" or "TERABOX_CLI_RESPONSE_INVALID";
    private string JournalPath(string operationId) => Path.Combine(accounts.Root, "downloads", TeraBoxStorage.Hash(operationId) + ".json");
    private static string Identity(CloudDownloadRequest request) => TeraBoxStorage.Hash(
        CloudSourceIdentity.Key(request.Source) + "\n" + Path.GetFullPath(request.OutputDirectory));
    private static TeraBoxShareLocator Validate(CloudSource source)
    {
        if (source.ProviderId != "terabox" || source.Kind != "share" || source.MethodId is not (null or "terabox.cli") ||
            !TeraBoxShareLocator.TryParse(source.Url, out var locator) || source.AccessCode is {Length: > 128} || source.AccessCode?.Any(char.IsControl) == true)
            throw new TeraBoxException("TERABOX_SOURCE_INVALID");
        return locator!;
    }
    private static void Validate(CloudDownloadRequest request)
    {
        Validate(request.Source);
        if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory) ||
            request.AccountId != null && request.Source.AccountId != null && request.AccountId != request.Source.AccountId)
            throw new TeraBoxException("TERABOX_OPERATION_CHANGED");
        TeraBoxStorage.NoLinks(request.OutputDirectory);
    }
    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        try { return await accounts.WithAccountAsync(accountId, null, async account =>
            { await account.RequireOnline(ct); return new CloudReadiness(true); }, ct); }
        catch (TeraBoxException error) { return new(false, error.Code); }
    }
    public async Task<CloudReadiness> CheckCanAbandonAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            Validate(request);
            var journal = await TeraBoxStorage.ReadAsync<Journal>(JournalPath(request.OperationId), ct);
            if (journal == null) return new(true);
            if (journal.Identity != Identity(request)) return new(false, "TERABOX_OPERATION_CHANGED");
            // This provider never writes remotely. A rejected payload request can leave a
            // submitted receipt and empty staging directories, which are safe to retain as
            // history when switching sources. Verified, partial or unknown outcomes are not.
            return !journal.Completed && journal.Files.Count > 0 && journal.Files.All(f =>
                f.State is "pending" or "submitted" && f.Sha256 == null && !HasPartial(request, f) &&
                !Path.Exists(TeraBoxStorage.Within(request.OutputDirectory, f.Remote.RelativePath)))
                ? new(true) : new(false, "TERABOX_DOWNLOAD_REVIEW_REQUIRED");
        }
        catch (TeraBoxException error) { return new(false, error.Code); }
    }
    public async Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            Validate(request);
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId,
                request.Source.AccountVersion, async account =>
                {
                    var journal = await TeraBoxStorage.ReadAsync<Journal>(JournalPath(request.OperationId), ct);
                    if (journal != null)
                    {
                        CheckIdentity(journal, request, account);
                        if (journal.Files.Any(f => f.State == "submitted" && HasPartial(request, f)))
                            return new(false, "TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                        if (journal.Completed && journal.Files.All(f => f.State == "completed"))
                            return await VerifyCompleted(request, journal, ct) ? new(true) : new(false, "TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                    }
                    await account.RequireOnline(ct);
                    var manifest = await ReadManifest(account, request.Source, ct);
                    if (journal != null && !Same(journal.Files.Select(f => f.Remote).ToList(), manifest)) return new(false, "TERABOX_SHARE_CHANGED");
                    return new CloudReadiness(true);
                }, ct), ct);
        }
        catch (TeraBoxException error) { return new(false, error.Code); }
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        var started = false;
        try
        {
            Validate(request); storage?.EnsurePathAllowed(request.OutputDirectory);
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId,
                request.Source.AccountVersion, async account =>
                {
                    var path = JournalPath(request.OperationId);
                    var journal = await TeraBoxStorage.ReadAsync<Journal>(path, ct);
                    if (journal != null)
                    {
                        CheckIdentity(journal, request, account);
                        started = journal.Files.Any(f => f.State != "pending");
                        await progress(new(null, Stage: "Verifying"));
                        if (journal.Completed && journal.Files.All(f => f.State == "completed"))
                        {
                            if (!await VerifyCompleted(request, journal, ct)) throw new TeraBoxException("TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                            return await Result(request, journal, progress);
                        }
                    }
                    await account.RequireOnline(ct);
                    var manifest = await ReadManifest(account, request.Source, ct);
                    if (journal == null)
                    {
                        RequireEmpty(request.OutputDirectory);
                        journal = new Journal {Identity = Identity(request), AccountId = account.Id, AccountVersion = account.Version,
                            Files = manifest.Select(f => new FileReceipt {Remote = f}).ToList()};
                        await TeraBoxStorage.WriteAsync(path, journal, ct);
                    }
                    else if (!Same(journal.Files.Select(f => f.Remote).ToList(), manifest)) throw new TeraBoxException("TERABOX_SHARE_CHANGED");
                    TeraBoxStorage.PayloadDirectory(request.OutputDirectory);
                    foreach (var file in journal.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var destination = TeraBoxStorage.Within(request.OutputDirectory, file.Remote.RelativePath);
                        var staging = StageFile(request, file);
                        if (file.State == "completed")
                        {
                            if (!await Verify(destination, file, ct)) throw new TeraBoxException("TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                            continue;
                        }
                        if (file.State == "verified")
                        {
                            if (File.Exists(destination))
                            { if (!await Verify(destination, file, ct) || File.Exists(staging)) throw new TeraBoxException("TERABOX_DOWNLOAD_REVIEW_REQUIRED"); }
                            else
                            {
                                if (!await Verify(staging, file, ct)) throw new TeraBoxException("TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                                TeraBoxStorage.PayloadDirectory(Path.GetDirectoryName(destination)!); File.Move(staging, destination, false);
                            }
                            file.State = "completed"; await TeraBoxStorage.WriteAsync(path, journal, ct); continue;
                        }
                        if (file.State is not ("pending" or "submitted") || HasPartial(request, file) || File.Exists(destination))
                            throw new TeraBoxException("TERABOX_DOWNLOAD_REVIEW_REQUIRED");
                        var stageDirectory = Path.GetDirectoryName(staging)!;
                        TeraBoxStorage.PayloadDirectory(stageDirectory);
                        file.State = "submitted"; await TeraBoxStorage.WriteAsync(path, journal, ct); started = true;
                        var arguments = ShareArguments("share-download", request.Source);
                        arguments.Insert(2, staging); arguments.Add("--fsid"); arguments.Add(file.Remote.Id);
                        await progress(new(null, CompletedBytes(journal), TotalBytes(journal), "Downloading"));
                        var result = await DownloadWithProgress(account, arguments, stageDirectory, staging, file, journal, progress, ct);
                        if (!TeraBoxJson.True(result, "success") || TeraBoxJson.Number(result, "size") != file.Remote.Size ||
                            TeraBoxJson.Text(result, "name") != Path.GetFileName(staging) ||
                            TeraBoxJson.Text(result, "local_path") is not { } reported || !Path.IsPathFullyQualified(reported) ||
                            Path.GetFullPath(reported) != staging || !File.Exists(staging))
                            throw new TeraBoxException("TERABOX_DOWNLOAD_RESPONSE_INVALID");
                        TeraBoxStorage.NoLinks(staging);
                        await progress(new(null, CompletedBytes(journal), TotalBytes(journal), "Verifying"));
                        var hash = await CloudPayloadHash.ComputeFileAsync(staging, ct);
                        if (hash.Length != file.Remote.Size) throw new TeraBoxException("TERABOX_DOWNLOAD_SIZE_MISMATCH");
                        file.Sha256 = hash.Sha256; file.State = "verified";
                        await TeraBoxStorage.WriteAsync(path, journal, ct);
                        TeraBoxStorage.PayloadDirectory(Path.GetDirectoryName(destination)!); File.Move(staging, destination, false);
                        file.State = "completed"; await TeraBoxStorage.WriteAsync(path, journal, ct);
                        // Only this operation's now-empty stage directories are reclaimed.
                        RemoveEmpty(stageDirectory);
                    }
                    // A directory can change while a large leaf is being downloaded.
                    // Only the same complete tree may become the durable final receipt.
                    // This API has no content hash/version, so same-size in-place changes
                    // remain an upstream limitation; local reuse always verifies SHA256.
                    if (!Same(manifest, await ReadManifest(account, request.Source, ct))) throw new TeraBoxException("TERABOX_SHARE_CHANGED");
                    journal.Completed = true; await TeraBoxStorage.WriteAsync(path, journal, ct);
                    RemoveEmpty(StageRoot(request));
                    return await Result(request, journal, progress);
                }, ct), ct);
        }
        catch (TeraBoxException error) { throw new CloudProviderException(error.Code, started); }
    }

    private static async Task<JsonElement> DownloadWithProgress(TeraBoxAccountService.AccountLease account,
        IReadOnlyList<string> arguments, string directory, string staging, FileReceipt file, Journal journal,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var download = account.Run(arguments, lifetime.Token, directory);
        try
        {
            while (!download.IsCompleted)
            {
                await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token));
                lifetime.Token.ThrowIfCancellationRequested();
                if (download.IsCompleted) break;
                TeraBoxStorage.NoLinks(staging);
                var current = File.Exists(staging) ? Math.Min(new FileInfo(staging).Length, file.Remote.Size) : 0;
                var completed = CompletedBytes(journal) + current; var total = TotalBytes(journal);
                await progress(new(total == 0 ? null : Math.Min(99, completed * 100d / total), completed, total, "Downloading"));
            }
            return await download;
        }
        finally
        {
            lifetime.Cancel();
            // Never let a cancelled UI/progress callback leave the CLI writing files.
            try { await download; } catch when (lifetime.IsCancellationRequested) { }
        }
    }

    internal static List<string> ShareArguments(string command, CloudSource source)
    {
        var locator = Validate(source);
        var args = new List<string> {command, locator.CanonicalUrl};
        var password = source.AccessCode ?? locator.Password;
        if (command != "share-info" && !string.IsNullOrEmpty(password)) { args.Add("--pwd"); args.Add(password); }
        return args;
    }
    internal sealed record RemoteFile(string Id, string RelativePath, long Size);
    internal static async Task<List<RemoteFile>> ReadManifest(TeraBoxAccountService.AccountLease account, CloudSource source, CancellationToken ct)
    {
        var info = await account.Run(ShareArguments("share-info", source), ct);
        var shareStatus = TeraBoxJson.Number(info, "status");
        // Native GetShareInfo preserves status independently and may legitimately
        // emit zero IDs for an unavailable share. Positive IDs matter for listing.
        if (shareStatus == 1) throw new TeraBoxException("TERABOX_SHARE_EXPIRED");
        if (shareStatus == 2) throw new TeraBoxException("TERABOX_SHARE_DELETED");
        if (shareStatus != 0) throw new TeraBoxException("TERABOX_SHARE_UNAVAILABLE");
        if (TeraBoxJson.Id(info, "share_id") == null || TeraBoxJson.Id(info, "uk") == null)
            throw new TeraBoxException("TERABOX_MANIFEST_INVALID");
        var files = new List<RemoteFile>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Queue<(string? Remote, string Relative, int Depth)>(); directories.Enqueue((null, "", 0));
        var pages = 0; long total = 0;
        while (directories.TryDequeue(out var directory))
        {
            if (directory.Depth > 32) throw new TeraBoxException("TERABOX_MANIFEST_LIMIT");
            for (var page = 1; ; page++)
            {
                if (++pages > 10000) throw new TeraBoxException("TERABOX_MANIFEST_LIMIT");
                var args = ShareArguments("share-list", source);
                args.AddRange(["--page", page.ToString(CultureInfo.InvariantCulture), "--num", "100", "--by", "name", "--order", "asc"]);
                if (directory.Remote != null) { args.Add("--dir"); args.Add(directory.Remote); }
                var result = await account.Run(args, ct);
                if (!result.TryGetProperty("files", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 100 ||
                    TeraBoxJson.Number(result, "total") != entries.GetArrayLength() ||
                    !result.TryGetProperty("has_more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    more.ValueKind == JsonValueKind.True && entries.GetArrayLength() == 0)
                    throw new TeraBoxException("TERABOX_MANIFEST_INVALID");
                foreach (var item in entries.EnumerateArray())
                {
                    if (ids.Count >= 100000) throw new TeraBoxException("TERABOX_MANIFEST_LIMIT");
                    var id = TeraBoxJson.Id(item, "fs_id");
                    var name = TeraBoxJson.Text(item, "name"); var remote = TeraBoxJson.Text(item, "path");
                    if (id == null || id == "0" || name == null || remote == null || !remote.StartsWith('/') || remote.EndsWith('/') ||
                        remote.Split('/').Skip(1).Any(s => s.Length == 0) || !item.TryGetProperty("is_dir", out var isDirectory) ||
                        isDirectory.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new TeraBoxException("TERABOX_MANIFEST_INVALID");
                    TeraBoxStorage.Segment(name);
                    foreach (var segment in remote.Split('/').Skip(1)) TeraBoxStorage.Segment(segment);
                    if (remote.Split('/').Last() != name || directory.Remote != null && remote != directory.Remote.TrimEnd('/') + "/" + name ||
                        !ids.Add(id)) throw new TeraBoxException("TERABOX_MANIFEST_CHANGED");
                    var relative = directory.Relative.Length == 0 ? name : directory.Relative + "/" + name;
                    if (!paths.Add(relative)) throw new TeraBoxException("TERABOX_MANIFEST_CHANGED");
                    if (isDirectory.ValueKind == JsonValueKind.True) directories.Enqueue((remote, relative, directory.Depth + 1));
                    else
                    {
                        var size = TeraBoxJson.Number(item, "size") ?? throw new TeraBoxException("TERABOX_MANIFEST_INVALID");
                        if (size > long.MaxValue - total) throw new TeraBoxException("TERABOX_MANIFEST_LIMIT");
                        total += size; files.Add(new(id, relative, size));
                    }
                }
                if (more.ValueKind == JsonValueKind.False) break;
            }
        }
        if (files.Count == 0) throw new TeraBoxException("TERABOX_EMPTY_SHARE");
        return files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
    }

    private static bool Same(IReadOnlyList<RemoteFile> left, IReadOnlyList<RemoteFile> right) => left.SequenceEqual(right);
    private static void CheckIdentity(Journal journal, CloudDownloadRequest request, TeraBoxAccountService.AccountLease account)
    {
        if (journal.Identity != Identity(request) || journal.AccountId != account.Id || journal.AccountVersion != account.Version || journal.Files.Count == 0)
            throw new TeraBoxException("TERABOX_OPERATION_CHANGED");
    }
    private static string StageRoot(CloudDownloadRequest request) => Path.Combine(request.OutputDirectory, ".bakabase-terabox-" + TeraBoxStorage.Hash(request.OperationId)[..16]);
    private static string StageFile(CloudDownloadRequest request, FileReceipt file) =>
        TeraBoxStorage.Within(StageRoot(request), file.Remote.Id + "/" + file.Remote.RelativePath.Split('/').Last());
    private static bool HasPartial(CloudDownloadRequest request, FileReceipt file)
    {
        var directory = Path.GetDirectoryName(StageFile(request, file))!;
        TeraBoxStorage.NoLinks(directory);
        return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any(p =>
            Path.GetFileName(p) != ".temporary" || !Directory.Exists(p) || Directory.EnumerateFileSystemEntries(p).Any());
    }
    private static void RequireEmpty(string directory)
    { if (File.Exists(directory) || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new TeraBoxException("TERABOX_OUTPUT_NOT_EMPTY"); }
    private static async Task<bool> Verify(string path, FileReceipt file, CancellationToken ct)
    {
        TeraBoxStorage.NoLinks(path);
        if (file.Sha256 == null || !File.Exists(path) || new FileInfo(path).Length != file.Remote.Size) return false;
        return (await CloudPayloadHash.ComputeFileAsync(path, ct)).Sha256 == file.Sha256;
    }
    private static async Task<bool> VerifyCompleted(CloudDownloadRequest request, Journal journal, CancellationToken ct)
    {
        foreach (var file in journal.Files) if (!await Verify(TeraBoxStorage.Within(request.OutputDirectory, file.Remote.RelativePath), file, ct)) return false;
        return true;
    }
    private static long TotalBytes(Journal journal) => journal.Files.Sum(f => f.Remote.Size);
    private static long CompletedBytes(Journal journal) => journal.Files.Where(f => f.State == "completed").Sum(f => f.Remote.Size);
    private static async Task<CloudDownloadResult> Result(CloudDownloadRequest request, Journal journal, Func<CloudDownloadProgress, Task> progress)
    {
        await progress(new(100, TotalBytes(journal), TotalBytes(journal), "Completed"));
        return new(journal.Files.Select(f => TeraBoxStorage.Within(request.OutputDirectory, f.Remote.RelativePath)).ToList());
    }
    private static void RemoveEmpty(string directory)
    {
        TeraBoxStorage.NoLinks(directory);
        if (!Directory.Exists(directory)) return;
        foreach (var child in Directory.EnumerateDirectories(directory)) RemoveEmpty(child);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    private async Task<T> WithOperation<T>(string id, Func<Task<T>> action, CancellationToken ct)
    { var gate = _operations.GetOrAdd(id, _ => new(1, 1)); await gate.WaitAsync(ct); try { return await action(); } finally { gate.Release(); } }
    private sealed class Journal
    {
        public required string Identity {get; init;}
        public required string AccountId {get; init;}
        public required string AccountVersion {get; init;}
        public required List<FileReceipt> Files {get; init;}
        public bool Completed {get; set;}
    }
    private sealed class FileReceipt
    {
        public required RemoteFile Remote {get; init;}
        public string State {get; set;} = "pending";
        public string? Sha256 {get; set;}
    }
}

public sealed class TeraBoxSourceAvailabilityProbe(TeraBoxAccountService accounts) : ISourceAvailabilityProbe
{
    public bool UsesAccountScope => true;
    public bool CanHandle(CloudSource source) => source.ProviderId == "terabox" && source.MethodId is null or "terabox.cli";
    public async Task<SourceAvailabilityResult> CheckAsync(CloudSource source, CancellationToken ct)
    {
        var status = SourceAvailabilityStatuses.Unknown; var code = "TERABOX_SHARE_UNAVAILABLE"; var evidence = new Dictionary<string, string>();
        try
        {
            await accounts.WithAccountAsync(source.AccountId, source.AccountVersion, async account =>
            {
                await account.RequireOnline(ct);
                var files = await TeraBoxCloudProvider.ReadManifest(account, source, ct);
                evidence["fileCount"] = files.Count.ToString(CultureInfo.InvariantCulture);
                evidence["totalBytes"] = files.Sum(f => f.Size).ToString(CultureInfo.InvariantCulture);
                return true;
            }, ct);
            status = SourceAvailabilityStatuses.Available; code = "TERABOX_SHARE_AVAILABLE";
        }
        catch (TeraBoxException error)
        {
            code = error.Code;
            status = code switch
            {
                "TERABOX_ACCOUNT_REQUIRED" or "TERABOX_ACCOUNT_CHANGED" or "TERABOX_ACCESS_CODE_REQUIRED" or "TERABOX_PERMISSION_REQUIRED" => SourceAvailabilityStatuses.AccessRequired,
                "TERABOX_NETWORK_UNAVAILABLE" or "TERABOX_SIGNATURE_EXPIRED" => SourceAvailabilityStatuses.TemporarilyUnavailable,
                "TERABOX_SHARE_EXPIRED" or "TERABOX_SHARE_DELETED" => SourceAvailabilityStatuses.ConfirmedUnavailable,
                _ => SourceAvailabilityStatuses.Unknown
            };
        }
        return new() {Status = status, ReasonCode = code, ProbeId = "terabox-cli", Evidence = evidence,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(status == SourceAvailabilityStatuses.Available ? 15 : 2)};
    }
}
