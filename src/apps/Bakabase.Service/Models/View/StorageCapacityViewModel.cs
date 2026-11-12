namespace Bakabase.Service.Models.View;

/// <summary>Capacity of the server volume containing a configured path; never a browser disk estimate.</summary>
public sealed record StorageCapacityViewModel(string Path, string Status, string? VolumePath = null,
    long? AvailableBytes = null, long? TotalBytes = null, string? ErrorCode = null);
