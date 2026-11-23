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
        long? Bytes, bool Incomplete, long? MinimumFreeBytes = null, AcquisitionCheckpointWrite? CheckpointWrite = null);
    internal static AcquisitionCheckpointWrite Bind(AcquisitionCheckpointWrite write, IAcquisitionBatchCapacityReader reader, IUserStoragePolicy storage)
    {
        storage.EnsurePathAllowed(write.StorageRoot);
        storage.EnsurePathAllowed(write.WritePath);
        ArtifactManifestFiles.RejectLinks(write.WritePath);
        var volume = Volume(write.StorageRoot);
        if (Volume(write.WritePath) != volume || write.VolumeId != null && write.VolumeId != volume)
            throw new IOException("LOCAL_BATCH_CAPACITY_UNKNOWN");
        return write with {VolumeId = volume};

        string Volume(string path)
        {
            var capacity = reader.Measure(path);
            var id = reader.Identity(capacity);
            if (string.IsNullOrWhiteSpace(id) || capacity.AvailableBytes < 0 || capacity.TotalBytes <= 0)
                throw new IOException("LOCAL_BATCH_CAPACITY_UNKNOWN");
            return id;
        }
    }

    internal static IReadOnlyList<AcquisitionLocalRecoveryBatchVolume> Inspect(IEnumerable<Item> items,
        long minimum, IAcquisitionBatchCapacityReader reader, IUserStoragePolicy storage)
    {
        var volumes = new Dictionary<string, Mutable>(ArtifactManifestFiles.PathComparer);
        foreach (var item in items)
        {
            var guarded = item.CheckpointWrite;
            if (guarded != null && (!item.Working.Contains(guarded.StorageRoot, ArtifactManifestFiles.PathComparer) ||
                guarded.VolumeId == null || guarded != Bind(guarded, reader, storage)))
                throw new IOException("LOCAL_BATCH_CAPACITY_UNKNOWN");
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
                    if (guarded != null && ArtifactManifestFiles.PathComparer.Equals(path, guarded.StorageRoot) && identity != guarded.VolumeId)
                        throw new IOException("LOCAL_BATCH_CAPACITY_UNKNOWN");
                    if (!volumes.TryGetValue(identity, out var volume))
                        volumes[identity] = volume = new(capacity.AvailableBytes, capacity.TotalBytes);
                    volume.Available = Math.Min(volume.Available, capacity.AvailableBytes);
                    volume.Minimum = Math.Max(volume.Minimum, item.MinimumFreeBytes ?? minimum);
                    volume.Paths.Add(path);
                    volume.IncludesCheckpoint |= guarded != null;
                    if (!seen.Add(identity)) continue;
                    if (item.Bytes is { } bytes && bytes >= 0)
                    {
                        volume.Incomplete |= bytes > long.MaxValue - volume.Bytes;
                        volume.Bytes = Sum(volume.Bytes, bytes);
                        if (guarded == null) volume.AdmissionBytes = Sum(volume.AdmissionBytes, bytes);
                        volume.HasKnown = true;
                    }
                    else volume.Incomplete = true;
                    volume.Incomplete |= item.Incomplete;
                }
            }
            if (guarded is {ExpandedBytes: >= 0})
            {
                var writer = volumes[guarded.VolumeId!];
                writer.AdmissionBytes = Sum(writer.AdmissionBytes, Sum(guarded.ExpandedBytes.Value, StorageWriteGuard.ReservationHeadroomBytes));
            }
        }
        return volumes.Select(p => new AcquisitionLocalRecoveryBatchVolume(p.Key, p.Value.Paths.ToArray(),
            p.Value.Available, p.Value.Total, p.Value.Minimum, Math.Max(0, p.Value.Available - p.Value.Minimum),
            p.Value.HasKnown ? p.Value.Bytes : null, p.Value.Incomplete,
            p.Value.Available <= p.Value.Minimum || p.Value.Bytes > Math.Max(0, p.Value.Available - p.Value.Minimum))
        {
            CurrentWritesExceedUsable = p.Value.IncludesCheckpoint
                ? p.Value.Available <= p.Value.Minimum || p.Value.AdmissionBytes > Math.Max(0, p.Value.Available - p.Value.Minimum) : null
        }).ToArray();
    }
    private static long Sum(long left, long right) => right > long.MaxValue - left ? long.MaxValue : left + right;

    private sealed class Mutable(long available, long total)
    {
        internal long Available = available;
        internal readonly long Total = total;
        internal long Bytes;
        internal long AdmissionBytes;
        internal long Minimum;
        internal bool HasKnown;
        internal bool IncludesCheckpoint;
        internal bool Incomplete;
        internal readonly HashSet<string> Paths = new(ArtifactManifestFiles.PathComparer);
    }
}
