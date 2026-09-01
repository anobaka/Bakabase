namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// What the fs.manualScan workflow trigger emits from the directories it enumerates.
/// </summary>
public enum FsScanTarget
{
    Files = 1,
    Directories = 2,
    Both = 3
}
