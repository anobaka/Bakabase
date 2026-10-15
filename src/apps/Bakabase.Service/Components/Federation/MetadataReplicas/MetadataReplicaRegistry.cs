using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record MetadataReplicaRegistration(string ServerId, string LibraryEpoch, string? Name,
    bool Denied = false, string? ErrorCode = null, string? Error = null);

/// <summary>Small identity registry only; resource data and checkpoints live in SQLite.</summary>
public sealed class MetadataReplicaRegistry(IFederationDataDirectory directory)
{
    private readonly SemaphoreSlim _gate = new(1);
    private string FilePath => Path.Combine(directory.Ensure(), "metadata-replicas", "owners.json");

    public async Task<IReadOnlyList<MetadataReplicaRegistration>> ReadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await ReadCoreAsync(ct); }
        finally { _gate.Release(); }
    }

    public async Task PutAsync(MetadataReplicaRegistration item, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var items = (await ReadCoreAsync(ct)).Where(x => x.ServerId != item.ServerId).Append(item).ToArray();
            if (items.Length > 1024) throw new ReplicaBudgetExceededException("Too many managed metadata libraries.");
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(items));
                    await stream.WriteAsync(bytes, ct);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }

    private async Task<MetadataReplicaRegistration[]> ReadCoreAsync(CancellationToken ct)
    {
        var path = FilePath;
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Metadata identity registry exceeds its limit.");
        return JsonConvert.DeserializeObject<MetadataReplicaRegistration[]>(await File.ReadAllTextAsync(path, ct)) ?? [];
    }
}
