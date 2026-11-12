using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;

namespace Bakabase.Service.Components.CloudDownloads.Rclone;

public sealed record RcloneDownloadSnapshot(string Identity, string ProfileVersion, IReadOnlyList<RcloneRemoteFile> Files);

/// <summary>Freezes discovery before transport; a retry of an operation cannot adopt a changed remote tree.</summary>
public sealed class RcloneDownloadSnapshotStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _operations = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RcloneDownloadSnapshotStore(AppService app) : this(Path.Combine(app.AppDataDirectory,
        "cloud-downloads", "rclone-snapshots")) { }
    internal RcloneDownloadSnapshotStore(string privateRoot) => _root = Path.GetFullPath(privateRoot);

    public async Task<RcloneDownloadSnapshot> GetOrCreateAsync(string providerId, string methodId, string operationId,
        string identity, string profileVersion, Func<CancellationToken, Task<IReadOnlyList<RcloneRemoteFile>>> create,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(identity) ||
            string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(methodId) ||
            !Guid.TryParseExact(profileVersion, "N", out _))
            throw new RcloneRemoteException("RCLONE_SNAPSHOT_INVALID");
        var key = CloudOperationService.Key(providerId, methodId, operationId);
        var gate = _operations.GetOrAdd(key, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var directory = Path.Combine(_root, key); RcloneRemoteEngine.RejectLinks(directory);
            var path = Path.Combine(directory, "manifest.json"); RcloneRemoteEngine.RejectLinks(path);
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new RcloneRemoteException("RCLONE_SNAPSHOT_INVALID");
                RcloneDownloadSnapshot? saved;
                try { saved = JsonSerializer.Deserialize<RcloneDownloadSnapshot>(await File.ReadAllTextAsync(path, ct), Json); }
                catch (JsonException) { throw new RcloneRemoteException("RCLONE_SNAPSHOT_INVALID"); }
                if (saved == null || saved.Files == null) throw new RcloneRemoteException("RCLONE_SNAPSHOT_INVALID");
                if (saved.Identity != identity) throw new RcloneRemoteException("RCLONE_OPERATION_CHANGED");
                if (saved.ProfileVersion != profileVersion) throw new RcloneRemoteException("RCLONE_ACCOUNT_CHANGED");
                var validated = RcloneRemoteEngine.ValidateManifest(saved.Files);
                if (validated.Count == 0) throw new RcloneRemoteException("RCLONE_EMPTY_REMOTE");
                return saved with {Files = validated};
            }
            var files = RcloneRemoteEngine.ValidateManifest(await create(ct));
            if (files.Count == 0) throw new RcloneRemoteException("RCLONE_EMPTY_REMOTE");
            var snapshot = new RcloneDownloadSnapshot(identity, profileVersion, files);
            var content = JsonSerializer.SerializeToUtf8Bytes(snapshot, Json);
            if (content.Length > 32 * 1024 * 1024) throw new RcloneRemoteException("RCLONE_MANIFEST_LIMIT");
            PrivateDirectory(_root); PrivateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = (UnixFileMode)0x180;
                await using (var stream = new FileStream(temporary, options)) await stream.WriteAsync(content, ct);
                RcloneRemoteEngine.RejectLinks(path); File.Move(temporary, path, false);
            }
            finally {if (File.Exists(temporary)) File.Delete(temporary);}
            return snapshot;
        }
        finally {gate.Release();}
    }

    private static void PrivateDirectory(string path)
    {
        RcloneRemoteEngine.RejectLinks(path); Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
    }
}
