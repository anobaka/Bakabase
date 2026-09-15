using Bakabase.Modules.Acquisition.Abstractions.Components;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>Read-only path checks shared by nodes that use configured server directories.</summary>
public static class AcquisitionConfigurationValidation
{
    public static IReadOnlyList<AcquisitionValidationIssue> Directory(string? path, string missingCode,
        string missingMessage, string missingMessageKey)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [new(missingCode, missingMessage, missingMessageKey)];
        try
        {
            if (Path.IsPathFullyQualified(path) && !File.Exists(Path.GetFullPath(path))) return [];
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Report a local configuration issue without touching the filesystem.
        }
        return [new("acquisition.directory.invalid", "Choose an absolute server directory path, not a file.",
            "workflow.validation.acquisition.directoryInvalid")];
    }
}
