using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Downloader.Components;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>Stores source output independently of the source queue and workflow execution.</summary>
public sealed class DownloadResultService
{
    private readonly BakabaseDbContext _db;
    private readonly Func<string> _appData;

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

    public async Task<DownloadResultDbModel> RecordTorrentAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, string torrentPath, int? workflowDefinitionId, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(torrentPath);
        var bytes = await TorrentMetadata.ReadBoundedAsync(stream, ct);
        TorrentMetadata.Validate(bytes);
        var fingerprint = Hash(bytes);
        var directory = System.IO.Path.Combine(_appData(), "downloader", "torrent-metadata");
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
            DownloadResultKind.TorrentMetadata, target, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(torrentPath))!, [target], fingerprint, workflowDefinitionId, ct);
    }

    public async Task<DownloadResultDbModel> RecordFilesAsync(int downloadTaskId, ThirdPartyId thirdPartyId,
        string sourceKey, string name, string directory, IReadOnlyCollection<string> files,
        int? workflowDefinitionId, CancellationToken ct = default)
    {
        var root = System.IO.Path.GetFullPath(directory);
        var paths = files.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) throw new ArgumentException("A local-files result must contain at least one file.", nameof(files));
        var entries = new List<string[]>();
        foreach (var path in paths)
        {
            var relative = System.IO.Path.GetRelativePath(root, path);
            if (System.IO.Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Every result file must be inside its download directory.", nameof(files));
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            entries.Add([relative.Replace('\\', '/'), hash]);
        }
        var fingerprint = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries)));
        return await RecordAsync(downloadTaskId, thirdPartyId, sourceKey, name, DownloadResultKind.LocalFiles,
            root, root, paths, fingerprint, workflowDefinitionId, ct);
    }

    private async Task<DownloadResultDbModel> RecordAsync(int taskId, ThirdPartyId thirdPartyId, string sourceKey,
        string name, DownloadResultKind kind, string path, string downloadDirectory, string[] files, string fingerprint,
        int? workflowDefinitionId, CancellationToken ct)
    {
        if (taskId <= 0 || string.IsNullOrWhiteSpace(sourceKey))
            throw new ArgumentException("A persisted download task and source key are required.");
        var key = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new object[]
            {taskId, (int) thirdPartyId, sourceKey, (int) kind, fingerprint})));
        var existing = await _db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(x => x.DeduplicationKey == key, ct);
        if (existing != null) return existing;
        var result = new DownloadResultDbModel
        {
            DownloadTaskId = taskId, ThirdPartyId = thirdPartyId, SourceKey = sourceKey, Name = name,
            Kind = kind, Path = path, DownloadDirectory = downloadDirectory, FilesJson = JsonSerializer.Serialize(files), Fingerprint = fingerprint,
            DeduplicationKey = key, WorkflowDefinitionId = workflowDefinitionId
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
            if (existing != null) return existing;
            throw;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
