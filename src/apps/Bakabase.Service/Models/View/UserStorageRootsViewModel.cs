namespace Bakabase.Service.Models.View;

public record UserStorageRootsViewModel(bool IsRestricted, UserStorageRootViewModel[] Roots);

public record UserStorageRootViewModel(string Path, string Name, string StorageKind, bool? ReadOnly);
