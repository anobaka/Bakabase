using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

public sealed partial class PikPakCliCloudProvider
{
    private async Task<CloudOwnedCopyRelease> PurgeTrashedFiles(PikPakCliAccountLease account,
        IReadOnlyDictionary<string, OwnedCopyEntry> files, OwnedCopyCleanup cleanup, string receiptPath,
        CloudVerifiedLocalDownload local, IReadOnlyList<string?> localGenerations, CancellationToken ct)
    {
        // Reconcile durable intents before acting. A restored/moved file can vanish from trash
        // without being deleted; require a file-ID lookup in addition to the full bin listing.
        while (cleanup.Purged.Count < files.Count)
        {
            var resuming = cleanup.PurgePending.Count != 0;
            var batch = resuming ? cleanup.PurgePending.ToArray() :
                files.Keys.Where(id => !cleanup.Purged.Contains(id)).Order(StringComparer.Ordinal).Take(100).ToArray();
            var entries = batch.Select(id => files[id]).ToArray();
            var inTrash = await FindInTrash(account, entries, ct);
            if (inTrash == null || !resuming && inTrash.Count != batch.Length)
                return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");

            foreach (var id in batch.Where(id => !inTrash.Contains(id)))
            {
                if (!await IsFileGone(account, id, ct))
                    return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");
                cleanup.Purged.Add(id);
            }
            cleanup.PurgePending = batch.Where(inTrash.Contains).ToList();
            if (!await LocalFilesStillVerified(local, localGenerations, ct))
                return Review("PIKPAK_OWNED_COPY_LOCAL_FILES_CHANGED");
            await PikPakCliStorage.WriteAsync(receiptPath, cleanup, ct);
            if (cleanup.PurgePending.Count == 0) continue;

            // Replaying after a restart is limited to exact IDs still in their proven bin records.
            // No directory, wildcard, or empty-bin operation can be submitted.
            var pending = cleanup.PurgePending.ToArray();
            try { await account.Process.PermanentlyDeleteFilesAsync(account.Profile, account.ProviderIdentity!, pending, ct); }
            catch (PikPakCliException error) when (error.Code == "PIKPAK_NETWORK_UNAVAILABLE")
            {
                // A lost HTTP reply may follow an accepted deletion.
            }

            var submitted = pending.Select(id => files[id]).ToArray();
            for (var attempt = 0; ; attempt++)
            {
                var remaining = await FindInTrash(account, submitted, ct);
                if (remaining == null) return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");
                if (remaining.Count == 0)
                {
                    foreach (var id in pending)
                        if (!await IsFileGone(account, id, ct))
                            return Review("PIKPAK_OWNED_COPY_REMOTE_FILES_CHANGED");
                    cleanup.Purged.UnionWith(pending);
                    cleanup.PurgePending.Clear();
                    await PikPakCliStorage.WriteAsync(receiptPath, cleanup, CancellationToken.None);
                    break;
                }
                if (attempt == 3) return new("retryable", "PIKPAK_OWNED_COPY_PURGE_UNCONFIRMED");
                await _restoreDelay(ct);
            }
        }
        return new("released");
    }

    private static async Task<bool> IsFileGone(PikPakCliAccountLease account, string id, CancellationToken ct)
    {
        try { await account.Run(["get", id, "--raw"], ct); return false; }
        catch (PikPakCliException error) when (error.Evidence?.ProviderCode == "FILE_NOT_FOUND") { return true; }
    }
}
