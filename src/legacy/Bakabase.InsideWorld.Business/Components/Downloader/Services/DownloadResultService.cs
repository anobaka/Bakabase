using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>Stores source output independently of the source queue and workflow execution.</summary>
public sealed partial class DownloadResultService
{
    private readonly BakabaseDbContext _db;
    private readonly Func<string> _appData;

    public ExHentaiDownloadLedger ExHentaiLedger => new(_appData);

    public DownloadResultService(BakabaseDbContext db, AppService appService)
        : this(db, () => appService.AppDataDirectory) { }

    internal DownloadResultService(BakabaseDbContext db, Func<string> appData)
    {
        _db = db;
        _appData = appData;
    }

    public async Task<DownloadResultDbModel?> GetAsync(int id, CancellationToken ct = default) =>
        await _db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<DownloadResultDbModel>> GetByTaskAsync(int downloadTaskId,
        CancellationToken ct = default) => await _db.DownloadResults.AsNoTracking()
        .Where(x => x.DownloadTaskId == downloadTaskId).OrderBy(x => x.Id).ToListAsync(ct);

    public async Task<DownloadResultDbModel?> GetLatestBySourceAsync(int downloadTaskId, string sourceKey,
        CancellationToken ct = default) => await _db.DownloadResults.AsNoTracking()
        .Where(x => x.DownloadTaskId == downloadTaskId && x.SourceKey == sourceKey)
        .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    public async Task<DownloadResultContents?> GetAvailableContentsAsync(DownloadResultDbModel result,
        CancellationToken ct = default) => DownloadResultContents.Resolve(result,
        await _db.Set<DownloadResultProcessingDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.DownloadResultId == result.Id, ct));

    public async Task<bool> CanReuseAsync(DownloadResultDbModel result, CancellationToken ct = default)
    {
        if (result.Kind == DownloadResultKind.LocalFiles)
            return await GetAvailableContentsAsync(result, ct) != null;
        return await ReadReusableTorrentMetadataAsync(result, ct) != null;
    }

    internal static async Task<byte[]?> ReadReusableTorrentMetadataAsync(DownloadResultDbModel result,
        CancellationToken ct, string? path = null)
    {
        if (result.Kind != DownloadResultKind.TorrentMetadata) return null;
        try
        {
            await using var stream = File.OpenRead(path ?? result.Path);
            var metadata = await TorrentMetadata.ReadBoundedAsync(stream, ct);
            TorrentMetadata.Validate(metadata);
            return string.Equals(Hash(metadata), result.Fingerprint, StringComparison.OrdinalIgnoreCase) ? metadata : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    public async Task<DownloadResultDbModel> RecordTorrentAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, string torrentPath, int? workflowDefinitionId, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(torrentPath);
        var bytes = await TorrentMetadata.ReadBoundedAsync(stream, ct);
        return await RecordTorrentMetadataAsync(downloadTaskId, thirdPartyId, sourceKey, name, bytes,
            Path.GetDirectoryName(Path.GetFullPath(torrentPath))!, workflowDefinitionId, false, torrentPath, ct);
    }

    /// <summary>Metadata storage and payload placement have independent paths; exporting a torrent is optional.</summary>
    public async Task<DownloadResultDbModel> RecordTorrentMetadataAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, byte[] bytes, string outputDirectory, int? workflowDefinitionId,
        bool requiresContentsDownload, string? exportedTorrentPath = null, CancellationToken ct = default)
    {
        TorrentMetadata.Validate(bytes);
        var fingerprint = Hash(bytes);
        var directory = AppDataStorage.Downloads.GetPath(_appData(), "torrent-metadata");
        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, fingerprint + ".torrent"));
        Directory.CreateDirectory(directory);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return await RecordAsync(downloadTaskId, thirdPartyId, sourceKey, name,
            DownloadResultKind.TorrentMetadata, target, Path.GetFullPath(outputDirectory),
            exportedTorrentPath == null ? [target] : [target, Path.GetFullPath(exportedTorrentPath)],
            fingerprint, workflowDefinitionId, ct, requiresContentsDownload);
    }

    public Task<DownloadResultDbModel> RecordFilesAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, string directory, IReadOnlyCollection<string> files,
        int? workflowDefinitionId, CancellationToken ct = default) =>
        RecordFilesAsync(downloadTaskId, thirdPartyId, sourceKey, name, directory, files,
            workflowDefinitionId, null, ct);

    /// <summary>Reports successful local hash reads, separately from provider transfer progress.</summary>
    public async Task<DownloadResultDbModel> RecordFilesAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, string directory, IReadOnlyCollection<string> files,
        int? workflowDefinitionId, Action<int>? onBytesRead, CancellationToken ct)
    {
        var root = System.IO.Path.GetFullPath(directory);
        var paths = files.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) throw new ArgumentException("A local-files result must contain at least one file.", nameof(files));
        ArtifactManifest manifest;
        try
        {
            manifest = await ArtifactManifestFiles.CaptureAsync(root, paths, path => new FileInfo(path),
                (stream, token) => ArtifactContentHash.ComputeAsync(stream, token, onBytesRead), ct, verifyTimestampChange: true);
        }
        catch (InvalidDataException error) { throw new ArgumentException(error.Message, nameof(files), error); }
        var fingerprint = manifest.Fingerprint;
        return await RecordAsync(downloadTaskId, thirdPartyId, sourceKey, name, DownloadResultKind.LocalFiles,
            root, root, paths, fingerprint, workflowDefinitionId, ct);
    }

    private async Task<DownloadResultDbModel> RecordAsync(int taskId, ThirdPartyId thirdPartyId, string sourceKey,
        string name, DownloadResultKind kind, string path, string downloadDirectory, string[] files, string fingerprint,
        int? workflowDefinitionId, CancellationToken ct, bool requiresContentsDownload = false)
    {
        if (taskId <= 0 || string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("A persisted download task and source key are required.");
        var key = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
            {taskId, (int) thirdPartyId, sourceKey, (int) kind, fingerprint})));
        var existing = await _db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == key, ct);
        if (existing != null) return await RefreshOutputAsync(existing, name, path, downloadDirectory, files, ct, requiresContentsDownload);
        var result = new DownloadResultDbModel
        {
            DownloadTaskId = taskId, ThirdPartyId = thirdPartyId, SourceKey = sourceKey, Name = name,
            Kind = kind, Path = path, DownloadDirectory = downloadDirectory, FilesJson = JsonSerializer.Serialize(files), Fingerprint = fingerprint,
            DeduplicationKey = key, WorkflowDefinitionId = workflowDefinitionId,
            RequiresContentsDownload = requiresContentsDownload
        };
        _db.DownloadResults.Add(result);
        try
        {
            await _db.SaveChangesAsync(ct);
            return result;
        }
        catch (DbUpdateException)
        {
            // Another producer may have recovered the same checkpoint concurrently.
            _db.Entry(result).State = EntityState.Detached;
            existing = await _db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == key, ct);
            if (existing != null) return await RefreshOutputAsync(existing, name, path, downloadDirectory, files, ct, requiresContentsDownload);
            throw;
        }
    }

    private async Task<DownloadResultDbModel> RefreshOutputAsync(DownloadResultDbModel existing, string name,
        string path, string directory, string[] files, CancellationToken ct, bool requiresContentsDownload)
    {
        // Reacquiring identical bytes may use a new root. Keep the durable result identity,
        // workflow binding and ownership, while repairing its source output locations.
        var json = JsonSerializer.Serialize(files);
        await _db.DownloadResults.Where(x => x.Id == existing.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Name, name).SetProperty(x => x.Path, path)
            .SetProperty(x => x.DownloadDirectory, directory).SetProperty(x => x.FilesJson, json)
            .SetProperty(x => x.RequiresContentsDownload, requiresContentsDownload), ct);
        existing.Name = name;
        existing.Path = path;
        existing.DownloadDirectory = directory;
        existing.FilesJson = json;
        existing.RequiresContentsDownload = requiresContentsDownload;
        return existing;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
