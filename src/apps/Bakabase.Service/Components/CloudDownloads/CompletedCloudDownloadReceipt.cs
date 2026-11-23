using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

internal sealed record CompletedDownloadPlacement(string Key, string Destination, string ContentsDirectory, string Fingerprint);

internal sealed record CompletedCloudDownloadReceipt(DownloadResultDbModel Result, string Destination, IReadOnlyList<string> Files)
{
    internal string Key => ReceiptKey(Result, Destination);

    private static string ReceiptKey(DownloadResultDbModel result, string destination) => CloudOperationService.Key(
        "completed-download-receipt.v1", result.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        result.DownloadTaskId.ToString(System.Globalization.CultureInfo.InvariantCulture), result.DownloadDirectory,
        result.FilesJson, result.Fingerprint, destination);

    internal static async Task<CompletedCloudDownloadReceipt> ObserveAsync(BakabaseDbContext db, int id, string expected,
        CancellationToken ct)
    {
        var native = await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var receipt = await db.DownloadResults.AsNoTracking().Where(r => r.DownloadTaskId == id)
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        if (native?.Status != DownloadTaskDbModelStatus.Complete || receipt == null || ReceiptKey(receipt, native.DownloadPath) != expected)
            throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
        string[] files;
        try { files = JsonSerializer.Deserialize<string[]>(receipt.FilesJson) ?? []; }
        catch (JsonException error) { throw new InvalidDataException("RECOVERY_FILES_UNVERIFIED", error); }
        if (files.Length is 0 or > 100000 || !CloudDownloadPayloadRoot.TryValidate(native.DownloadPath, receipt.DownloadDirectory, files, out _))
            throw new InvalidDataException("RECOVERY_FILES_UNVERIFIED");
        return new(receipt, native.DownloadPath, files);
    }

    internal CompletedDownloadPlacement Placement => new(Key, Destination, Result.DownloadDirectory, Result.Fingerprint);

    internal void RequireManifest(string root, ArtifactManifest manifest) =>
        RequireManifest(Result.DownloadDirectory, Result.Fingerprint, root, manifest);

    internal static void RequireManifest(string contentsDirectory, string fingerprint, string root, ArtifactManifest manifest)
    {
        // A presentation layout can add a parent directory. Hash exactly the original
        // receipt's relative paths and the captured per-file content hashes.
        var originalEntries = manifest.Entries.Select(entry => entry with
            {RelativePath = ArtifactManifestFiles.RelativeFile(contentsDirectory, Path.Combine(root, entry.RelativePath))})
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal);
        if (ArtifactManifestFiles.Fingerprint(originalEntries) != fingerprint)
            throw new InvalidDataException("RECOVERY_FILES_UNVERIFIED");
    }
}
