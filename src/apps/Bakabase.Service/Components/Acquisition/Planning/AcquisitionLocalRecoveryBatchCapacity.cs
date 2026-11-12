using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.Acquisition.Planning;

public interface IAcquisitionBatchCapacityReader
{
    StorageVolumeCapacity Measure(string path);
    string Identity(StorageVolumeCapacity capacity);
}
public sealed class AcquisitionBatchCapacityReader : IAcquisitionBatchCapacityReader
{
    public StorageVolumeCapacity Measure(string path) => StorageVolumes.Measure(path);
    public string Identity(StorageVolumeCapacity capacity) => StorageVolumes.CapacityIdentity(capacity);
}

/// <summary>Measured bytes are an estimate, not an archive expansion guarantee. No expected
/// cleanup, clone savings or future free space is credited in this admission calculation.</summary>
internal static class AcquisitionLocalRecoveryBatchCapacity
{
    internal sealed record Item(IReadOnlyList<string> Working, IReadOnlyList<string> Delivery,
        long? Bytes, bool Incomplete, long? MinimumFreeBytes = null);
    internal static IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> Inspect(IEnumerable<Item> items,
        long minimum, IAcquisitionBatchCapacityReader reader, IUserStoragePolicy storage)
    {
        var volumes = new Dictionary<string, Mutable>(ArtifactManifestFiles.PathComparer);
        foreach (var item in items)
        {
            // A processing result and a delivery copy may coexist. Each purpose contributes
            // once per volume, even when several nested/part paths resolve to that volume.
            foreach (var paths in new[] {item.Working, item.Delivery})
            {
                var seen = new HashSet<string>(ArtifactManifestFiles.PathComparer);
                foreach (var path in paths.Distinct(ArtifactManifestFiles.PathComparer))
                {
                    storage.EnsurePathAllowed(path);
                    ArtifactManifestFiles.RejectLinks(path);
                    var capacity = reader.Measure(path);
                    var identity = reader.Identity(capacity);
                    if (string.IsNullOrWhiteSpace(identity) || capacity.AvailableBytes < 0 || capacity.TotalBytes <= 0)
                        throw new IOException("LOCAL_BATCH_CAPACITY_UNKNOWN");
                    if (!volumes.TryGetValue(identity, out var volume))
                        volumes[identity] = volume = new(capacity.AvailableBytes, capacity.TotalBytes);
                    volume.Available = Math.Min(volume.Available, capacity.AvailableBytes);
                    volume.Minimum = Math.Max(volume.Minimum, item.MinimumFreeBytes ?? minimum);
                    volume.Paths.Add(path);
                    if (!seen.Add(identity)) continue;
                    if (item.Bytes is { } bytes && bytes >= 0)
                    {
                        try {volume.Bytes = checked(volume.Bytes + bytes);}
                        catch (OverflowException) {volume.Incomplete = true; volume.Bytes = long.MaxValue;}
                        volume.HasKnown = true;
                    }
                    else volume.Incomplete = true;
                    volume.Incomplete |= item.Incomplete;
                }
            }
        }
        return volumes.Select(p => new AcquisitionLocalRecoveryBatchVolume(p.Key, p.Value.Paths.ToArray(),
            p.Value.Available, p.Value.Total, p.Value.Minimum, Math.Max(0, p.Value.Available - p.Value.Minimum),
            p.Value.HasKnown ? p.Value.Bytes : null, p.Value.Incomplete,
            p.Value.Available <= p.Value.Minimum || p.Value.Bytes > Math.Max(0, p.Value.Available - p.Value.Minimum))).ToArray();
    }
    private sealed class Mutable(long available, long total)
    {
        internal long Available = available;
        internal readonly long Total = total;
        internal long Bytes;
        internal long Minimum;
        internal bool HasKnown;
        internal bool Incomplete;
        internal readonly HashSet<string> Paths = new(ArtifactManifestFiles.PathComparer);
    }
}
