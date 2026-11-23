using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Downloader.Cloud;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

public sealed partial class DownloadResultService
{
    internal async Task RecordVerifiedExistingFilesAsync(DownloadTaskDbModel original,
        CloudExistingDownloadVerification proof, Func<CancellationToken, Task> validate, CancellationToken ct,
        Func<Func<Task>, Task>? guardRegistration = null)
    {
        if (original.Status is not (DownloadTaskDbModelStatus.Failed or DownloadTaskDbModelStatus.Disabled) || original.CompletedAt != null ||
            proof.Files.Count is 0 or > 10000 || proof.Files.Any(f => f.Length < 0 || f.Sha256?.Length != 64))
            throw new InvalidOperationException("RECOVERY_EXISTING_FILES_UNVERIFIED");
        var root = Path.GetFullPath(proof.PayloadRootDirectory ?? original.DownloadPath);
        // A provider cannot register an unrelated tree or external paths as this task's result.
        var relative = Path.GetRelativePath(Path.GetFullPath(original.DownloadPath), root);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        var files = proof.Files.Select(f => Path.GetFullPath(f.Path)).ToArray();
        // The provider has already checked every member against its own remote checksums.
        // The local receipt independently reopens and rehashes a timestamp-only change.
        var manifest = await ArtifactManifestFiles.CaptureAsync(root, files, path => new FileInfo(path),
            ct: ct, verifyTimestampChange: true);
        var expected = proof.Files.ToDictionary(f => Path.GetFullPath(f.Path), ArtifactManifestFiles.PathComparer);
        foreach (var item in manifest.Entries)
        {
            var file = expected[Path.GetFullPath(Path.Combine(root, item.RelativePath))];
            if (file.Length != item.Length || !string.Equals(file.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("RECOVERY_EXISTING_FILES_UNVERIFIED");
        }
        await validate(ct);
        Task Register() => CommitVerifiedExistingFilesAsync(original, root, files, manifest.Fingerprint, ct);
        if (guardRegistration == null) await Register();
        else await guardRegistration(Register);
    }

    private async Task CommitVerifiedExistingFilesAsync(DownloadTaskDbModel original, string root, string[] files,
        string fingerprint, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        DownloadResultDbModel? result = null;
        try
        {
            var current = await _db.DownloadTasks.AsNoTracking().SingleAsync(t => t.Id == original.Id, ct);
            if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(original) ||
                await _db.DownloadResults.AnyAsync(r => r.DownloadTaskId == original.Id, ct) ||
                await _db.DownloadResultOwners.AnyAsync(r => r.DownloadTaskId == original.Id, ct))
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            // All files and the original request were verified; register the result and status
            // together. Rollback leaves the original status and historical message untouched.
            result = await RecordAsync(original.Id, original.ThirdPartyId, original.Key,
                original.Name ?? original.Key, DownloadResultKind.LocalFiles, root, root, files,
                fingerprint, null, ct);
            var changed = await _db.DownloadTasks.Where(t => t.Id == original.Id &&
                t.Status == original.Status && t.CompletedAt == null &&
                t.Options == original.Options && t.DownloadPath == original.DownloadPath &&
                t.Checkpoint == original.Checkpoint && t.Progress == original.Progress && t.Message == original.Message)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, DownloadTaskDbModelStatus.Complete)
                    .SetProperty(t => t.LastFileVerificationJson, (string?)null)
                    .SetProperty(t => t.Progress, 100).SetProperty(t => t.CompletedAt, DateTime.UtcNow)
                    .SetProperty(t => t.DownloadStatusUpdateDt, DateTime.Now), ct);
            if (changed != 1) throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (result != null) _db.Entry(result).State = EntityState.Detached;
            throw;
        }
    }
}
