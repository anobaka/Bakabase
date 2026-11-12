namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Stable discovery outcomes; presentation and recovery must not classify arbitrary tool text.</summary>
public static class ArchiveFormatProbeFailureCodes
{
    public const string Incomplete = "ArchiveProbeIncomplete";
    public const string TimedOut = "ArchiveProbeTimedOut";
    public const string BudgetExceeded = "ArchiveDetectionBudgetExceeded";
    public const string OutputLimit = "ArchiveProbeOutputLimit";
    public const string PrefixLimit = "ArchiveProbePrefixLimit";
    public const string ToolFailed = "ArchiveProbeToolFailed";
    // A saved legacy detector result may be rechecked with the bounded current detector.
    // This does not assert that the selected file contains an extractable archive.
    public const string UpgradeRequired = "ArchiveProbeUpgradeRequired";
    public const string PasswordRequired = "PasswordUnknown";
}
