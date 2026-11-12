namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>Explicit opening modes for a selected archive; null preserves normal detection.</summary>
public enum ArchiveOpenMode
{
    /// <summary>7-Zip's # parser exposes embedded containers and intervening byte segments.</summary>
    SevenZipParser = 1
}
