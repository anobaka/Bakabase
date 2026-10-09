namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>User-selected paths, separate from application-owned database/cache I/O.</summary>
public interface IUserStoragePolicy
{
    bool IsRestricted { get; }
    IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles);
    bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles);
    void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles);
    /// <summary>Checks a directory entry before moving, renaming or deleting the whole directory.</summary>
    void EnsureTreeMutationAllowed(string path);
}

public enum UserStoragePurpose
{
    UserFiles,
    Setup
}

public sealed record UserStorageRoot(string Path, string Name, string StorageKind, bool? ReadOnly);
