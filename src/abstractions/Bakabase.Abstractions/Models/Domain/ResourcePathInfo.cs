namespace Bakabase.Abstractions.Models.Domain;

public record ResourcePathInfo(
    string Path,
    string RelativePath,
    string[] MediaLibraryPathSegments,
    string[] RelativePathSegments,
    string[] InnerPaths,
    bool IsFile);