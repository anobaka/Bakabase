namespace Bakabase.Abstractions.Components.Gui;

/// <summary>Optional desktop capability. The user selects the destination before any write.</summary>
public interface ILocalFileSaveDialog
{
    Task<LocalFileSaveOutcome> SaveTextFileAsync(string suggestedFileName, string text,
        CancellationToken cancellationToken = default);
}

public enum LocalFileSaveOutcome
{
    Unavailable,
    Cancelled,
    Saved
}
