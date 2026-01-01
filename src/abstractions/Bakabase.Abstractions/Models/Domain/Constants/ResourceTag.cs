namespace Bakabase.Abstractions.Models.Domain.Constants;

[Flags]
public enum ResourceTag
{
    IsParent = 1 << 0,
    Pinned = 1 << 1,
    [Obsolete]
    PathDoesNotExist = 1 << 2,
    [Obsolete]
    UnknownMediaLibrary = 1 << 3
}