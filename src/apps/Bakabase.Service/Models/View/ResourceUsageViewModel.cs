using System;

namespace Bakabase.Service.Models.View;

public record ResourceUsageViewModel
{
    /// <summary>Service process CPU, normalized to the processors available to this process (0–100).</summary>
    public double? CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
    /// <summary>Sum of file lengths under the effective data directory, excluding symbolic links.</summary>
    public long? DataDirectoryBytes { get; init; }
    public DateTimeOffset? DataDirectoryUpdatedAt { get; init; }
    public bool DataDirectoryScanning { get; init; }
    public bool DataDirectoryPartial { get; init; }
    public bool DataDirectoryUnavailable { get; init; }
}
