namespace Bakabase.Abstractions.Models.Domain;

public static class ResourceProfileScope
{
    public static void Validate(string? sourceDeviceId, string? sourceLibraryEpoch, string? openingMode,
        string? nameTemplate, ResourceProfileEnhancerOptions? enhancerOptions,
        ResourceProfilePlayableFileOptions? playableFileOptions, ResourceProfilePropertyOptions? propertyOptions)
    {
        if (openingMode is not (null or "browser" or "native" or "ask"))
            throw new ArgumentException("Choose browser, native, ask, or inherit for the opening mode.");
        if (sourceDeviceId != null && (string.IsNullOrWhiteSpace(sourceDeviceId) || sourceDeviceId.Length > 128 ||
            sourceDeviceId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
            throw new ArgumentException("Choose a valid source device.");
        if (sourceDeviceId != null && string.IsNullOrWhiteSpace(sourceLibraryEpoch))
            throw new ArgumentException("Refresh the source device's library identity before saving this profile.");
        if (sourceLibraryEpoch != null && (sourceDeviceId == null || string.IsNullOrWhiteSpace(sourceLibraryEpoch) ||
            sourceLibraryEpoch.Length > 128 || sourceLibraryEpoch.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
            throw new ArgumentException("A library epoch must belong to a source device.");
        if (sourceDeviceId != null && (nameTemplate != null || enhancerOptions != null ||
            playableFileOptions != null || propertyOptions != null))
            throw new ArgumentException("Profiles for another device can only configure players and opening mode.");
    }
}
