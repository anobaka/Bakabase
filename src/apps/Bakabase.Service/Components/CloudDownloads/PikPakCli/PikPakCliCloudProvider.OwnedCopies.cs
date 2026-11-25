using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider : ICloudOwnedCopyProvider
{
    public async Task<bool> IsOwnedCopyEnabledAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        if (request.Source.Kind != "share" || request.RemoteEntry != null ||
            string.IsNullOrWhiteSpace(request.OperationId)) return false;

        try
        {
            return await accounts.WithAccountAsync(request.AccountId ?? request.Source.AccountId,
                request.Source.AccountVersion, account => Task.FromResult(
                    account.CanManageFiles && account.TrashTemporaryCopiesAfterDownload), ct);
        }
        catch (PikPakCliException) { return false; }
    }

    public async Task<CloudOwnedCopyReceipt?> PrepareOwnedCopyAsync(CloudDownloadRequest request, CancellationToken ct)
    {
        Validate(request.Source);
        if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) ||
            !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new CloudProviderException("PIKPAK_OPERATION_CHANGED");

        try
        {
            return await WithOperation(request.OperationId, () =>
                accounts.WithAccountAsync<CloudOwnedCopyReceipt?>(request.AccountId ?? request.Source.AccountId,
                    request.Source.AccountVersion, account => accounts.WithTransferAsync(account, async () =>
                    {
                        if (!account.TrashTemporaryCopiesAfterDownload || !account.CanManageFiles) return null;
                        var output = Path.GetFullPath(request.OutputDirectory);
                        PikPakCliStorage.NoLinks(output);
                        storage?.EnsurePathAllowed(output);
                        var saved = await ReadJournal(request.OperationId, ct);
                        if (saved is {EnrolledOwnedCopy: false})
                        {
                            if (!BeforeRemoteWrite(saved) ||
                                saved.FolderName != "Bakabase-" + PikPakCliStorage.Hash(request.OperationId) ||
                                await PikPakCliStorage.ReadAsync<DownloadJournal>(
                                    Path.Combine(OperationPath(request.OperationId), "download.json"), ct) != null)
                                return null;
                            CheckIdentity(saved, request.Source, account);
                        }

                        await account.RequireOnline(ct);
                        if (saved is {EnrolledOwnedCopy: false})
                        {
                            saved.EnrolledOwnedCopy = true;
                            await SaveJournal(request.OperationId, saved, ct);
                        }
                        var journal = await EnsureRestored(request.OperationId, request.Source, account, ct, enrollOwnedCopy: true);
                        return OwnedReceipt(journal);
                    }, ct), ct), ct);
        }
        catch (PikPakCliException error) { throw new CloudProviderException(error.Code, true) {CanWaitForCapacity = error.CanWaitForCapacity}; }
    }

    public async Task<CloudOwnedCopyRelease> ReleaseOwnedCopyAsync(CloudDownloadRequest request,
        CloudOwnedCopyReceipt copy, CloudVerifiedLocalDownload local, CancellationToken ct)
    {
        try
        {
            Validate(request.Source);
            if (request.RemoteEntry != null || string.IsNullOrWhiteSpace(request.OperationId) ||
                !Path.IsPathFullyQualified(request.OutputDirectory))
                return Review("PIKPAK_OWNED_COPY_CHANGED");

            return await WithOperation(request.OperationId, () =>
                accounts.WithAccountAsync(copy.AccountId, copy.AccountVersion, account =>
                    accounts.WithTransferAsync(account, () => ReleaseAsync(request, copy, local, account, ct), ct), ct), ct);
        }
        catch (PikPakCliException error) when (error.Code == "PIKPAK_NETWORK_UNAVAILABLE")
        { return new("retryable", error.Code); }
        catch (PikPakCliException error) { return Review(error.Code); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Review("PIKPAK_OWNED_COPY_PROOF_UNAVAILABLE"); }
    }

    private async Task<CloudOwnedCopyRelease> ReleaseAsync(CloudDownloadRequest request, CloudOwnedCopyReceipt copy,
        CloudVerifiedLocalDownload local, PikPakCliAccountLease account, CancellationToken ct)
    {
        if (!account.TrashTemporaryCopiesAfterDownload) return new("retained");
        if (!account.CanManageFiles) return Review("PIKPAK_MANAGE_FILES_SCOPE_REQUIRED");
        if ((request.AccountId ?? request.Source.AccountId) is { } pinned && pinned != account.Id ||
            request.Source.AccountVersion is { } generation && generation != account.Version)
            return Review("PIKPAK_OWNED_COPY_CHANGED");

        var journal = await ReadJournal(request.OperationId, ct);
        if (journal == null || OwnedReceipt(journal) != copy)
            return Review("PIKPAK_OWNED_COPY_CHANGED");
        CheckIdentity(journal, request.Source, account);

        var state = OperationPath(request.OperationId);
        var sourceProof = await PikPakCliStorage.ReadAsync<DownloadJournal>(Path.Combine(state, "download.json"), ct);
        if (!MatchesLocalDownload(request, journal, sourceProof, local))
            return Review("PIKPAK_OWNED_COPY_LOCAL_FILES_CHANGED");

        var receiptPath = Path.Combine(state, "remote-cleanup.json");
        var owned = journal.OwnedEntries!;
        var expectedFiles = owned.Where(entry => !entry.IsFolder).ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var cleanup = await PikPakCliStorage.ReadAsync<OwnedCopyCleanup>(receiptPath, ct) ?? new()
        {
            Copy = copy,
            VerificationFingerprint = local.Fingerprint,
            DownloadTaskId = local.DownloadTaskId,
            DownloadResultId = local.DownloadResultId,
        };
        if (!cleanup.Proves(copy, local, expectedFiles.Keys))
            return Review("PIKPAK_OWNED_COPY_CHANGED");
        if (cleanup.Trashed.Count == expectedFiles.Count) return new("trashed");

        var localGenerations = local.VerifiedFiles.Select(file =>
            file.ContentGeneration ?? ArtifactFileIdentity.ReadContentGeneration(file.Path)).ToArray();
        await account.RequireOnline(ct);
        while (true)
        {
            var actual = await ReadOwnedTree(account, copy.ContainerId, journal.FolderName, ct);
            if (actual == null) return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");
            var excluded = cleanup.Trashed.ToHashSet(StringComparer.Ordinal);
            if (cleanup.Pending.Count > 0)
            {
                // Submission can reach PikPak before the CLI or process can acknowledge it.
                // Both absence from the original tree and an exact bin record are required.
                var pending = cleanup.Pending.Select(id => expectedFiles[id]).ToArray();
                if (actual.Any(entry => cleanup.Pending.Contains(entry.Id)) ||
                    !await IsInTrash(account, pending, ct))
                    return new("retryable", "PIKPAK_OWNED_COPY_TRASH_UNCONFIRMED");
                excluded.UnionWith(cleanup.Pending);
            }

            if (!Same(owned.Where(entry => !excluded.Contains(entry.Id)).ToArray(), actual))
                return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");

            if (cleanup.Pending.Count > 0)
            {
                cleanup.Trashed.UnionWith(cleanup.Pending);
                cleanup.Pending.Clear();
                await PikPakCliStorage.WriteAsync(receiptPath, cleanup, CancellationToken.None);
            }
            if (cleanup.Trashed.Count == expectedFiles.Count) return new("trashed");

            // The CLI trashes a folder recursively and does not condition that operation on
            // the folder being empty. Preserve all directories and trash only proven file IDs.
            var pendingIds = expectedFiles.Keys.Where(id => !cleanup.Trashed.Contains(id))
                .Order(StringComparer.Ordinal).Take(100).ToArray();
            if (!await LocalFilesStillVerified(local, localGenerations, ct))
                return Review("PIKPAK_OWNED_COPY_LOCAL_FILES_CHANGED");
            cleanup.Pending.AddRange(pendingIds);
            await PikPakCliStorage.WriteAsync(receiptPath, cleanup, ct);
            try { await account.Run(["rm", ..pendingIds], ct); }
            catch (PikPakCliException)
            {
                // The next iteration only reconciles this durably submitted batch.
            }
        }
    }

    private static CloudOwnedCopyRelease Review(string reason) => new("reviewRequired", reason);

    private static CloudOwnedCopyReceipt? OwnedReceipt(Journal journal)
    {
        if (!journal.EnrolledOwnedCopy || journal.State != "restored" || journal.FolderId == null ||
            journal.CreatedOwnedFolderId != journal.FolderId || journal.OwnedEntries is not {Count: > 0} ||
            !journal.Files.SequenceEqual(journal.OwnedEntries.Where(entry => !entry.IsFolder)
                .Select(entry => new RemoteFile(entry.Id, entry.Path, entry.Size, entry.Hash, entry.Version))))
            return null;
        return new(journal.AccountId, journal.AccountVersion, journal.FolderId, PikPakCliStorage.Hash(
            JsonSerializer.Serialize(new {journal.FolderName, journal.SourceIdentity, journal.Selection, journal.OwnedEntries},
                PikPakCliStorage.Json)));
    }

    private static bool ProvesOwnedEntries(IReadOnlyList<OwnedCopyEntry> entries, IReadOnlyList<ShareNode> selected,
        string shareId)
    {
        static IEnumerable<string> Directories(IEnumerable<ShareNode> nodes, string prefix)
        {
            foreach (var node in nodes.Where(node => node.IsFolder))
            {
                var path = prefix + node.Name;
                yield return path;
                foreach (var child in Directories(node.Children, path + "/")) yield return child;
            }
        }

        return entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == entries.Count &&
               entries.Where(entry => entry.IsFolder).Select(entry => entry.Path).Order(StringComparer.Ordinal)
                   .SequenceEqual(Directories(selected, "").Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
               entries.Where(entry => !entry.IsFolder).All(entry => entry.OriginalShareId == shareId &&
                   !string.IsNullOrWhiteSpace(entry.Hash) && !string.IsNullOrWhiteSpace(entry.Version));
    }

    private static async Task<OwnedCopyEntry[]?> ReadOwnedTree(PikPakCliAccountLease account, string folderId,
        string expectedFolderName, CancellationToken ct)
    {
        var root = (await ListAccount(account, "", ct)).SingleOrDefault(entry => entry.Id == folderId);
        if (root is not {IsFolder: true} || root.Name != expectedFolderName) return null;

        var members = new List<OwnedCopyEntry>();
        await ReadRemote(account, folderId, ct, members);
        return members.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    private static bool MatchesLocalDownload(CloudDownloadRequest request, Journal journal, DownloadJournal? proof,
        CloudVerifiedLocalDownload actual)
    {
        if (proof == null || actual.DownloadTaskId <= 0 || actual.DownloadResultId <= 0 ||
            string.IsNullOrWhiteSpace(actual.Fingerprint)) return false;
        var output = Path.GetFullPath(request.OutputDirectory);
        var identity = DownloadIdentity(request, output, journal);
        if (proof.Identity != identity || actual.Directory != output ||
            proof.Completed.Count != journal.Files.Count || actual.VerifiedFiles.Count != journal.Files.Count ||
            actual.Files.Count != journal.Files.Count || journal.Files.Count == 0)
            return false;

        var files = actual.VerifiedFiles.ToDictionary(file => file.Path, StringComparer.Ordinal);
        if (!files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(actual.Files)) return false;
        return journal.Files.All(file =>
        {
            var path = PikPakCliStorage.Within(output, file.Path);
            return proof.Completed.TryGetValue(file.Path, out var sealedFile) && files.TryGetValue(path, out var verified) &&
                   sealedFile.Size == file.Size && verified.Length == file.Size &&
                   sealedFile.Sha256.Equals(verified.Sha256, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static async Task<bool> LocalFilesStillVerified(CloudVerifiedLocalDownload local,
        IReadOnlyList<string?> generations, CancellationToken ct)
    {
        for (var index = 0; index < local.VerifiedFiles.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var file = local.VerifiedFiles[index];
            PikPakCliStorage.NoLinks(file.Path);
            var info = new FileInfo(file.Path);
            if (!info.Exists || info.Length != file.Length ||
                file.LastWriteTimeUtc is { } written && info.LastWriteTimeUtc != written) return false;
            if (generations[index] is { } generation)
            {
                if (ArtifactFileIdentity.ReadContentGeneration(file.Path) != generation) return false;
            }
            else if (!string.Equals(await PikPakCliStorage.FileHash(file.Path, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static async Task<bool> IsInTrash(PikPakCliAccountLease account, IReadOnlyList<OwnedCopyEntry> pending,
        CancellationToken ct)
    {
        var unverified = pending.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var token = "";
        do
        {
            if (!seen.Add(token) || seen.Count > 10000) return false;
            var arguments = new List<string> {"trash", "ls", "--limit", "100"};
            if (token.Length != 0) { arguments.Add("--token"); arguments.Add(token); }
            var page = await account.Run(arguments, ct);
            if (!page.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array) return false;
            foreach (var file in files.EnumerateArray())
            {
                var id = PikPakCliJson.Text(file, "id");
                if (id == null || !unverified.TryGetValue(id, out var expected)) continue;
                if (PikPakCliJson.Text(file, "parent_id") != expected.ParentId ||
                    PikPakCliJson.Text(file, "kind") is not ("file" or "drive#file") ||
                    PikPakCliJson.Text(file, "name") != expected.Path.Split('/')[^1]) return false;
                unverified.Remove(id);
            }
            if (unverified.Count == 0) return true;
            token = PikPakCliJson.Text(page, "next_page_token") ?? "";
            if (token.Length > 8192 || token.Length != 0 && files.GetArrayLength() == 0) return false;
        } while (token.Length != 0);
        return false;
    }

    private sealed record OwnedCopyEntry(string Id, string ParentId, string Path, bool IsFolder, long Size,
        string Hash, string Version, string? OriginalShareId);

    private sealed class OwnedCopyCleanup
    {
        public CloudOwnedCopyReceipt? Copy { get; set; }
        public string VerificationFingerprint { get; set; } = "";
        public int DownloadTaskId { get; set; }
        public int DownloadResultId { get; set; }
        public HashSet<string> Trashed { get; set; } = new(StringComparer.Ordinal);
        public List<string> Pending { get; set; } = [];

        public bool Proves(CloudOwnedCopyReceipt copy, CloudVerifiedLocalDownload local, IEnumerable<string> files)
        {
            var expected = files.ToHashSet(StringComparer.Ordinal);
            return Copy == copy && VerificationFingerprint == local.Fingerprint &&
                   DownloadTaskId == local.DownloadTaskId && DownloadResultId == local.DownloadResultId &&
                   Trashed != null && Pending != null && Trashed.IsSubsetOf(expected) &&
                   Pending.Distinct(StringComparer.Ordinal).Count() == Pending.Count &&
                   Pending.All(id => expected.Contains(id) && !Trashed.Contains(id));
        }
    }
}
