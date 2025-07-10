namespace Bakabase.Abstractions.Models.Domain;

public record ResourcePathInfo(
    string Path,
    string RelativePath,
    string[] RelativePathSegments,
    string[] InnerPaths,
    bool IsFile);