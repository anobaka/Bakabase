using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

/// <summary>Official share restore and original-file download. Every remote write has a durable
/// intent and a private destination; an unknown result is reconciled, never blindly repeated.</summary>
public sealed class PikPakCliCloudProvider : ICloudDownloadProvider, ICloudDownloadRecoveryProvider, ICloudDownloadAbandonmentProvider
{
    private readonly PikPakCliAccountService accounts;
    private readonly IUserStoragePolicy? storage;
    private readonly Func<CancellationToken, Task> _restoreDelay;
    public PikPakCliCloudProvider(PikPakCliAccountService accounts, IUserStoragePolicy? storage = null)
        : this(accounts, storage, ct => Task.Delay(TimeSpan.FromSeconds(2), ct)) { }
    internal PikPakCliCloudProvider(PikPakCliAccountService accounts, IUserStoragePolicy? storage, Func<CancellationToken, Task> restoreDelay)
    { this.accounts = accounts; this.storage = storage; _restoreDelay = restoreDelay; }
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operations = new(StringComparer.Ordinal);
    public string ProviderId => "pikpak";
    public string MethodId => "pikpak.share";
    bool ICloudDownloadRecoveryProvider.RevalidationChecksReadiness => true;
    public bool CanCorrectAccessCodeAfterFailure(string code) => code == "PIKPAK_ACCESS_CODE_REQUIRED";
    public bool CanRevalidateDownloadFailure(string code) => code is "PIKPAK_ORIGINAL_FILE_UNAVAILABLE" or
        "PIKPAK_PUBLIC_ACCESS_REQUIRED" or "PIKPAK_VERIFICATION_REQUIRED" or "PIKPAK_ACCOUNT_REQUIRED" or
        "PIKPAK_PERMISSION_REQUIRED" or "PIKPAK_ACCESS_CODE_REQUIRED" or "PIKPAK_NETWORK_UNAVAILABLE" or "PIKPAK_RESTORE_PENDING" or
        "PIKPAK_STORAGE_QUOTA_EXCEEDED" or "PIKPAK_RESTORE_REVIEW_REQUIRED";

    public async Task<CloudReadiness> CheckCanAbandonAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.OperationId)) return new(false, "PIKPAK_OPERATION_CHANGED");
            var journal = await ReadJournal(request.OperationId, ct);
            // This capability is deliberately local and read-only. A recorded folder intent
            // already means that abandoning the source could orphan a remote side effect.
            if (journal == null) return new(true);
            if (journal.SourceIdentity != SourceIdentity(request.Source)) return new(false, "PIKPAK_OPERATION_CHANGED");
            return journal.State == "planned" && journal.FolderId == null && journal.Files is {Count: 0} &&
                   journal.Attempts is {Count: 0} && journal.RestoreTaskId == null && journal.RestoreStatus == null
                ? new(true) : new(false, "PIKPAK_RESTORE_REVIEW_REQUIRED");
        }
        catch (PikPakCliException error) { return new(false, error.Code); }
    }

    public async Task<CloudReadiness> CheckReadyAsync(string? accountId, CancellationToken ct)
    {
        try { return await accounts.WithAccountAsync(accountId, null, async account =>
            { await account.RequireOnline(ct); return new CloudReadiness(true); }, ct); }
        catch (PikPakCliException error) { return new(false, error.Code); }
    }

    public async Task<CloudReadiness> RevalidateDownloadAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        try
        {
            Validate(request.Source);
            if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
                return new(false, "PIKPAK_OPERATION_CHANGED");
            PikPakCliStorage.NoLinks(request.OutputDirectory);
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId, request.Source.AccountVersion, account =>
                accounts.WithTransferAsync(account, async () =>
            {
                var status = await account.ReadStatusAsync(ct);
                var journal = await ReadJournal(request.OperationId, ct);
                if (journal != null) CheckIdentity(journal, request.Source, account);
                if (journal is {State: "restoreSubmitted" or "restored"})
                {
                    if (journal.FolderId == null) return new(false, "PIKPAK_RESTORE_REVIEW_REQUIRED");
                    IReadOnlyList<RemoteFile> remote;
                    try { remote = await ReadRemote(account, journal.FolderId, ct); }
                    catch (PikPakCliException error) when (error.Code == "PIKPAK_RESTORE_PENDING")
                    { return new(false, UnresolvedRestoreProblem(journal)); }
                    if (!MatchesSelection(journal.Selection, remote)) return new(false, UnresolvedRestoreProblem(journal));
                    if (journal.Files.Count != 0 && !Same(journal.Files, remote)) return new(false, "PIKPAK_REMOTE_CHANGED");
                    // The user's verified saved copy remains usable after the original
                    // public share expires. No second restore or original-share call is needed.
                    return new CloudReadiness(true);
                }
                if (journal is {State: "folderCreating"}) return new(false, "PIKPAK_RESTORE_REVIEW_REQUIRED");
                var selection = await ReadShare(account, request.Source, ct);
                if (journal != null && !Same(journal.Selection, selection)) return new(false, "PIKPAK_SHARE_CHANGED");
                if (CheckCapacity(status, selection).IsInsufficient)
                    return new(false, "PIKPAK_STORAGE_QUOTA_EXCEEDED");
                // A metadata check never saves shares or opens payload URLs. Even an old
                // anonymous failed task can therefore use this without changing its history.
                return new CloudReadiness(true);
            }, ct), ct), ct);
        }
        catch (PikPakCliException error) { return new(false, error.Code); }
    }

    public async Task<CloudDownloadResult> DownloadAsync(CloudDownloadRequest request,
        Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        Validate(request.Source);
        if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new CloudProviderException("PIKPAK_OPERATION_CHANGED");
        var output = Path.GetFullPath(request.OutputDirectory); PikPakCliStorage.NoLinks(output); storage?.EnsurePathAllowed(output);
        try
        {
            return await WithOperation(request.OperationId, () => accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId, request.Source.AccountVersion, async account =>
            {
                await account.RequireOnline(ct);
                await progress(new(null, Stage: "Saving shared originals to the authorized PikPak drive"));
                var journal = await accounts.WithTransferAsync(account,
                    () => EnsureRestored(request.OperationId, request.Source, account, ct), ct);
                if (!Same(journal.Files, await ReadRemote(account, journal.FolderId!, ct)))
                    throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
                return await DownloadFiles(request, output, journal, account, progress, ct);
            }, ct), ct);
        }
        catch (PikPakCliException error) { throw new CloudProviderException(error.Code, true); }
    }

    private async Task<T> WithOperation<T>(string operationId, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = _operations.GetOrAdd(operationId, _ => new(1, 1)); await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }

    private async Task<CloudDownloadResult> DownloadFiles(CloudDownloadRequest request, string output, Journal journal,
        PikPakCliAccountLease account, Func<CloudDownloadProgress, Task> progress, CancellationToken ct)
    {
        var state = OperationPath(request.OperationId);
        var copies = await PikPakCliStorage.ReadAsync<DownloadJournal>(Path.Combine(state, "download.json"), ct);
        var identity = PikPakCliStorage.Hash(JsonSerializer.Serialize(new {request.OperationId, output,
            journal.AccountId, journal.AccountVersion, journal.SourceIdentity, journal.Files}, PikPakCliStorage.Json));
        if (copies != null && copies.Identity != identity) throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        copies ??= new() {Identity = identity};
        await PikPakCliStorage.WriteAsync(Path.Combine(state, "download.json"), copies, ct);
        Directory.CreateDirectory(output);
        var stage = Path.Combine(output, ".bakabase-pikpak-" + PikPakCliStorage.Hash(request.OperationId));
        PikPakCliStorage.Directory(stage);
        long total = journal.Files.Aggregate(0L, (value, file) => checked(value + file.Size));
        long completed = 0; var result = new List<string>();
        foreach (var file in journal.Files)
        {
            ct.ThrowIfCancellationRequested();
            var destination = PikPakCliStorage.Within(output, file.Path);
            var part = Path.Combine(stage, PikPakCliStorage.Hash(file.Id) + ".payload"); PikPakCliStorage.NoLinks(part);
            copies.Completed.TryGetValue(file.Path, out var receipt);
            if (File.Exists(destination))
            {
                await progress(new(null, Stage: "Verifying"));
                if (receipt == null || !await MatchesFile(destination, file, receipt, ct)) throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
            }
            else
            {
                if (Directory.Exists(destination)) throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
                if (receipt != null)
                {
                    await progress(new(null, Stage: "Verifying"));
                    if (!File.Exists(part) || !await MatchesFile(part, file, receipt, ct)) throw new PikPakCliException("PIKPAK_LOCAL_CHANGED");
                }
                else
                {
                    // CLI parallel downloads may preallocate their file. Size alone does not
                    // prove a cancelled attempt completed; preserve it for explicit recovery.
                    if (File.Exists(part) || copies.Started.Contains(file.Id)) throw new PikPakCliException("PIKPAK_PARTIAL_DOWNLOAD_REVIEW_REQUIRED");
                    copies.Started.Add(file.Id);
                    await PikPakCliStorage.WriteAsync(Path.Combine(state, "download.json"), copies, ct);
                    await progress(new(total == 0 ? null : 100d * completed / total, completed, total, "Downloading PikPak original files"));
                    await account.Run(["download", file.Id, "--output", part], ct, payload: true);
                    if (!File.Exists(part) || new FileInfo(part).Length != file.Size) throw new PikPakCliException("PIKPAK_DOWNLOAD_INCOMPLETE");
                    PikPakCliStorage.NoLinks(part);
                    await progress(new(null, Stage: "Verifying"));
                    receipt = new(file.Size, await PikPakCliStorage.FileHash(part, ct));
                    copies.Completed[file.Path] = receipt;
                    await PikPakCliStorage.WriteAsync(Path.Combine(state, "download.json"), copies, CancellationToken.None);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); PikPakCliStorage.Within(output, file.Path);
                File.Move(part, destination, overwrite: false);
            }
            completed += file.Size; result.Add(destination);
            await progress(new(total == 0 ? 100d * result.Count / journal.Files.Count : 100d * completed / total,
                completed, total, "Downloading PikPak original files"));
        }
        var temporary = Path.Combine(stage, "temporary"); PikPakCliStorage.NoLinks(temporary);
        if (Directory.Exists(temporary) && !Directory.EnumerateFileSystemEntries(temporary).Any()) Directory.Delete(temporary);
        if (!Directory.EnumerateFileSystemEntries(stage).Any()) Directory.Delete(stage);
        if (!Same(journal.Files, await ReadRemote(account, journal.FolderId!, ct)))
            throw new PikPakCliException("PIKPAK_REMOTE_CHANGED");
        CloudDownloadManifest.Validate(output, result);
        return new(result);
    }

    private async Task<Journal> EnsureRestored(string operationId, CloudSource source, PikPakCliAccountLease account, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        var journal = await ReadJournal(operationId, ct);
        if (journal == null)
        {
            var selection = await ReadShare(account, source, ct);
            journal = new() {AccountId = account.Id, AccountVersion = account.Version, SourceIdentity = SourceIdentity(source),
                Selection = selection, FolderName = "Bakabase-" + PikPakCliStorage.Hash(operationId), State = "planned"};
            await SaveJournal(operationId, journal, ct);
        }
        CheckIdentity(journal, source, account);
        if (journal.State == "restored") return journal;
        if (journal.State != "restoreSubmitted" && !Same(journal.Selection, await ReadShare(account, source, ct)))
            throw new PikPakCliException("PIKPAK_SHARE_CHANGED");
        if (journal.State == "planned") await RequireCapacity(operationId, journal, account, ct);
        if (journal.FolderId == null)
        {
            var matches = (await ListAccount(account, "", ct)).Where(i => i.Name == journal.FolderName).ToArray();
            if (matches.Length != 0)
            {
                if (journal.State != "folderCreating" || matches.Length != 1 || !matches[0].IsFolder)
                    throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
                journal.FolderId = matches[0].Id;
            }
            else
            {
                if (journal.State == "folderCreating") throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
                journal.State = "folderCreating"; await SaveJournal(operationId, journal, ct);
                var folder = await account.Run(["mkdir", journal.FolderName], ct);
                journal.FolderId = PikPakCliJson.Required(folder, "id");
                ValidateId(journal.FolderId);
            }
            journal.State = "folderReady"; await SaveJournal(operationId, journal, CancellationToken.None);
        }
        if (journal.State == "folderReady")
        {
            if ((await ListAccount(account, journal.FolderId, ct)).Count != 0) throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
            await RequireCapacity(operationId, journal, account, ct);
            PikPakShareLocator.TryParse(source.Url, out var locator);
            var arguments = new List<string> {"share", "restore", locator!.ShareId, "--file-ids",
                string.Join(',', journal.Selection.Select(f => f.Id)), "--parent", journal.FolderId};
            Password(arguments, source);
            var submission = new RestoreAttempt {StartedAt = DateTimeOffset.UtcNow, Outcome = "unknown"};
            journal.Attempts.Add(submission);
            journal.State = "restoreSubmitted"; await SaveJournal(operationId, journal, ct);
            try
            {
                var result = await account.Run(arguments, ct);
                journal.RestoreTaskId = PikPakCliJson.Text(result, "restore_task_id");
                if (journal.RestoreTaskId != null) ValidateId(journal.RestoreTaskId);
                journal.RestoreStatus = SafeRestoreStatus(PikPakCliJson.Text(result, "restore_status"));
                submission.Outcome = "responded";
                submission.FinishedAt = DateTimeOffset.UtcNow;
                await SaveJournal(operationId, journal, CancellationToken.None);
            }
            // Even an authorization error may come from a CLI follow-up after the server
            // accepted a save. Keep submitted intent on every failure; only reconciliation
            // can establish its result. Never infer safe re-submission from diagnostic text.
            catch (Exception error)
            {
                submission.FinishedAt = DateTimeOffset.UtcNow;
                submission.Problem = error is PikPakCliException failure ? failure.Code :
                    error is OperationCanceledException ? "PIKPAK_OPERATION_INTERRUPTED" : "PIKPAK_CLI_REQUEST_FAILED";
                submission.Evidence = (error as PikPakCliException)?.Evidence;
                await SaveJournal(operationId, journal, CancellationToken.None);
                throw;
            }
        }
        IReadOnlyList<RemoteFile> remote = [];
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try { remote = await ReadRemote(account, journal.FolderId, ct); }
            catch (PikPakCliException error) when (error.Code == "PIKPAK_RESTORE_PENDING") { remote = []; }
            if (MatchesSelection(journal.Selection, remote)) break;
            if (UnresolvedRestoreProblem(journal) == "PIKPAK_RESTORE_REVIEW_REQUIRED")
                throw new PikPakCliException("PIKPAK_RESTORE_REVIEW_REQUIRED");
            if (attempt == 7) throw new PikPakCliException(UnresolvedRestoreProblem(journal));
            await _restoreDelay(ct);
        }
        journal.Files = remote; journal.State = "restored";
        await SaveJournal(operationId, journal, CancellationToken.None);
        return journal;
    }

    private static string? SafeRestoreStatus(string? status) => status is "OK" or "COMPLETE" or "PENDING" or "RUNNING" ? status : null;
    private static string UnresolvedRestoreProblem(Journal journal) =>
        journal.Attempts.LastOrDefault()?.Outcome == "responded" || journal.RestoreTaskId != null ||
        journal.RestoreStatus is "OK" or "COMPLETE" or "PENDING" or "RUNNING"
            ? "PIKPAK_RESTORE_PENDING" : "PIKPAK_RESTORE_REVIEW_REQUIRED";

    private static PikPakCliCapacity.Check CheckCapacity(JsonElement status, IReadOnlyList<ShareNode> selection) =>
        PikPakCliCapacity.Evaluate(status, Flatten(selection).Select(file => file.Size));

    private async Task RequireCapacity(string operationId, Journal journal, PikPakCliAccountLease account, CancellationToken ct)
    {
        var check = CheckCapacity(await account.ReadStatusAsync(ct), journal.Selection);
        journal.CapacityChecks.Add(check);
        await SaveJournal(operationId, journal, ct);
        if (check.IsInsufficient) throw new PikPakCliException("PIKPAK_STORAGE_QUOTA_EXCEEDED");
    }

    internal static async Task<IReadOnlyList<ShareNode>> ReadShare(PikPakCliAccountLease account, CloudSource source, CancellationToken ct)
    {
        Validate(source); PikPakShareLocator.TryParse(source.Url, out var locator);
        var visited = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        async Task<IReadOnlyList<ShareNode>> Read(string parent, int depth, int selectedIndex)
        {
            if (depth > 32 || !visited.Add(parent)) throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
            var entries = new List<ShareNode>(); var tokens = new HashSet<string>(); var token = "";
            var selected = selectedIndex < locator!.NodeIds.Count ? locator.NodeIds[selectedIndex] : null;
            do
            {
                if (!tokens.Add(token) || tokens.Count > 10000) throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
                var arguments = new List<string> {"share", "get", locator.ShareId, "--limit", "100"};
                if (parent.Length != 0) { arguments.Add("--parent"); arguments.Add(parent); }
                if (token.Length != 0) { arguments.Add("--token"); arguments.Add(token); }
                Password(arguments, source);
                var page = await account.Run(arguments, ct);
                var status = PikPakCliJson.Text(page, "status");
                if (ShareStatusProblem(status) is { } problem) throw new PikPakCliException(problem);
                if (!(page.TryGetProperty(parent.Length == 0 ? "file_list" : "files", out var items) && items.ValueKind == JsonValueKind.Array))
                    throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
                foreach (var item in items.EnumerateArray())
                {
                    if (++count > 100000) throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
                    var id = PikPakCliJson.Required(item, "id"); ValidateId(id);
                    if (selected != null && id != selected) continue;
                    var kind = PikPakCliJson.Required(item, "kind");
                    if (kind is not ("folder" or "file")) throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
                    var name = PikPakCliStorage.Segment(PikPakCliJson.Required(item, "name"));
                    if (kind == "file" && selected != null && selectedIndex + 1 < locator.NodeIds.Count)
                        throw new PikPakCliException("PIKPAK_SELECTED_PATH_INVALID");
                    var children = kind == "folder" ? await Read(id, depth + 1, selected == null ? selectedIndex : selectedIndex + 1) : [];
                    if (selected != null && selectedIndex + 1 < locator.NodeIds.Count) entries.AddRange(children);
                    else entries.Add(new(id, name, kind == "folder", PikPakCliJson.Text(item, "size"), children));
                }
                token = PikPakCliJson.Text(page, "next_page_token") ?? "";
                if (token.Length > 8192 || token.Length != 0 && items.GetArrayLength() == 0) throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
            } while (token.Length != 0);
            if (selected != null && entries.Count == 0) throw new PikPakCliException("PIKPAK_SELECTED_PATH_INVALID");
            if (entries.Select(i => i.Id).Distinct().Count() != entries.Count || entries.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
                throw new PikPakCliException("PIKPAK_SHARE_MANIFEST_INVALID");
            return entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray();
        }
        var result = await Read("", 0, 0);
        if (result.Count is 0 or > 1000 || !Flatten(result).Any()) throw new PikPakCliException("PIKPAK_EMPTY_SHARE");
        return result;
    }

    private static async Task<IReadOnlyList<AccountNode>> ListAccount(PikPakCliAccountLease account, string parent, CancellationToken ct)
    {
        var result = new List<AccountNode>(); var token = ""; var seen = new HashSet<string>();
        do
        {
            if (!seen.Add(token) || seen.Count > 10000) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
            var arguments = new List<string> {"ls", "--raw", "--limit", "100"};
            if (parent.Length != 0) { arguments.Add("--parent"); arguments.Add(parent); }
            if (token.Length != 0) { arguments.Add("--token"); arguments.Add(token); }
            var page = await account.Run(arguments, ct);
            if (!page.TryGetProperty("files", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
            foreach (var item in items.EnumerateArray())
            {
                if (result.Count >= 100000) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
                var id = PikPakCliJson.Required(item, "id"); ValidateId(id);
                var kind = PikPakCliJson.Required(item, "kind");
                if (kind is not ("drive#file" or "drive#folder")) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
                if (PikPakCliJson.Text(item, "phase") is { } phase && phase != "PHASE_TYPE_COMPLETE") throw new PikPakCliException("PIKPAK_RESTORE_PENDING");
                result.Add(new(id, PikPakCliStorage.Segment(PikPakCliJson.Required(item, "name")), kind == "drive#folder",
                    kind == "drive#folder" ? 0 : PikPakCliJson.Number(item, "size") ?? throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID"),
                    PikPakCliJson.Text(item, "hash") ?? "", PikPakCliJson.Text(item, "modified_time") ?? ""));
            }
            token = PikPakCliJson.Text(page, "next_page_token") ?? "";
            if (token.Length > 8192 || token.Length != 0 && items.GetArrayLength() == 0) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
        } while (token.Length != 0);
        if (result.Select(i => i.Id).Distinct().Count() != result.Count || result.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
        return result;
    }

    private static async Task<IReadOnlyList<RemoteFile>> ReadRemote(PikPakCliAccountLease account, string folder, CancellationToken ct)
    {
        var files = new List<RemoteFile>(); var seen = new HashSet<string>(); var visited = 0;
        async Task Read(string parent, string prefix, int depth)
        {
            if (depth > 32 || !seen.Add(parent)) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
            foreach (var item in await ListAccount(account, parent, ct))
            {
                if (++visited > 100000) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
                var path = prefix + item.Name;
                if (item.IsFolder) await Read(item.Id, path + "/", depth + 1);
                else files.Add(new(item.Id, path, item.Size, item.Hash, item.Version));
            }
        }
        await Read(folder, "", 0);
        if (files.Select(f => f.Id).Distinct().Count() != files.Count || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
            throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID");
        return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }

    internal static bool MatchesSelection(IReadOnlyList<ShareNode> selection, IReadOnlyList<RemoteFile> files)
    {
        var expected = Flatten(selection).OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
        return expected.Length != 0 && expected.Length == files.Count && expected.Zip(files.OrderBy(f => f.Path, StringComparer.Ordinal))
            .All(pair => pair.First.Path == pair.Second.Path && DisplaySizeMatches(pair.First.Size, pair.Second.Size));
    }
    // CLI JSON uses presentation labels for some statuses; the anonymous API uses enums.
    // Normalize only verified values. Unknown statuses never establish invalidity.
    internal static string? ShareStatusProblem(string? status) => status switch
    {
        null or "OK" => null,
        "PASS_CODE_EMPTY" or "PASS_CODE_ERROR" or "Need Password" or "Wrong Password" => "PIKPAK_ACCESS_CODE_REQUIRED",
        "EXPIRED" or "DELETED" or "NOT_FOUND" or "Expired" or "Deleted" or "Not Found" => "PIKPAK_SHARE_EXPIRED",
        _ => "PIKPAK_SHARE_UNAVAILABLE"
    };
    internal static bool DisplaySizeMatches(string? display, long size)
    {
        var bounds = PikPakCliCapacity.Parse(display);
        return bounds != null && size >= bounds.Minimum && size <= bounds.Maximum;
    }
    private static IEnumerable<(string Path, string? Size)> Flatten(IEnumerable<ShareNode> nodes, string prefix = "")
    {
        foreach (var node in nodes)
            if (node.IsFolder) { foreach (var file in Flatten(node.Children, prefix + node.Name + "/")) yield return file; }
            else yield return (prefix + node.Name, node.DisplaySize);
    }
    private static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a, PikPakCliStorage.Json) == JsonSerializer.Serialize(b, PikPakCliStorage.Json);
    private static void Validate(CloudSource source)
    {
        if (source.ProviderId != "pikpak" || source.Kind != "share" || !PikPakShareLocator.TryParse(source.Url, out var locator))
            throw new PikPakCliException("PIKPAK_SOURCE_INVALID");
        ValidateId(locator!.ShareId);
    }
    private static void ValidateId(string id)
    { if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant)) throw new PikPakCliException("PIKPAK_REMOTE_MANIFEST_INVALID"); }
    private static void Password(List<string> arguments, CloudSource source)
    {
        var code = source.AccessCode;
        if (string.IsNullOrEmpty(code))
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(source.Url).Query);
            code = query.TryGetValue("pwd", out var pwd) ? pwd.ToString() : query.TryGetValue("pass_code", out var value) ? value.ToString() : null;
        }
        if (string.IsNullOrEmpty(code)) return;
        if (code.Length > 256 || code.Any(char.IsControl)) throw new PikPakCliException("PIKPAK_ACCESS_CODE_REQUIRED");
        arguments.Add("--password"); arguments.Add(code);
    }
    private string OperationPath(string id) => Path.Combine(accounts.Root, "operations", PikPakCliStorage.Hash(id));
    private Task<Journal?> ReadJournal(string id, CancellationToken ct) => PikPakCliStorage.ReadAsync<Journal>(Path.Combine(OperationPath(id), "transfer.json"), ct);
    private Task SaveJournal(string id, Journal journal, CancellationToken ct) => PikPakCliStorage.WriteAsync(Path.Combine(OperationPath(id), "transfer.json"), journal, ct);
    private static string SourceIdentity(CloudSource source) => PikPakCliStorage.Hash(JsonSerializer.Serialize(new {source.ProviderId, source.Url, source.AccessCode}));
    private static void CheckIdentity(Journal journal, CloudSource source, PikPakCliAccountLease account)
    {
        if (journal.AccountId != account.Id || journal.AccountVersion != account.Version) throw new PikPakCliException("PIKPAK_ACCOUNT_CHANGED");
        if (journal.SourceIdentity != SourceIdentity(source)) throw new PikPakCliException("PIKPAK_OPERATION_CHANGED");
        if (journal.State is not ("planned" or "folderCreating" or "folderReady" or "restoreSubmitted" or "restored") ||
            journal.Attempts == null || journal.Files == null || journal.Selection == null || journal.CapacityChecks == null ||
            journal.State == "planned" && journal.FolderId != null ||
            (journal.State is "planned" or "folderReady") && journal.Attempts.Count != 0)
            throw new PikPakCliException("PIKPAK_STATE_INVALID");
    }
    private static async Task<bool> MatchesFile(string path, RemoteFile file, FileReceipt receipt, CancellationToken ct) =>
        new FileInfo(path).Length == file.Size && receipt.Size == file.Size && receipt.Sha256 == await PikPakCliStorage.FileHash(path, ct);
    internal sealed record ShareNode(string Id, string Name, bool IsFolder, string? DisplaySize, IReadOnlyList<ShareNode> Children);
    internal sealed record RemoteFile(string Id, string Path, long Size, string Hash, string Version);
    private sealed record AccountNode(string Id, string Name, bool IsFolder, long Size, string Hash, string Version);
    private sealed record FileReceipt(long Size, string Sha256);
    private sealed class Journal
    {
        public Journal() { }
        public string AccountId { get; set; } = "";
        public string AccountVersion { get; set; } = "";
        public string SourceIdentity { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string? FolderId { get; set; }
        public string State { get; set; } = "planned";
        public string? RestoreTaskId { get; set; }
        public string? RestoreStatus { get; set; }
        public IReadOnlyList<ShareNode> Selection { get; set; } = [];
        public IReadOnlyList<RemoteFile> Files { get; set; } = [];
        public List<PikPakCliCapacity.Check> CapacityChecks { get; set; } = [];
        public List<RestoreAttempt> Attempts { get; set; } = [];
    }
    private sealed class RestoreAttempt
    {
        public RestoreAttempt() { }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string Outcome { get; set; } = "unknown";
        public string? Problem { get; set; }
        public PikPakCliFailureEvidence? Evidence { get; set; }
    }
    private sealed class DownloadJournal
    {
        public DownloadJournal() { }
        public string Identity { get; set; } = "";
        public HashSet<string> Started { get; set; } = [];
        public Dictionary<string, FileReceipt> Completed { get; set; } = new(StringComparer.Ordinal);
    }
}
