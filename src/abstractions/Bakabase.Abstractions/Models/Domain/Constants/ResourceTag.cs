namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum ResourceTag
{
    IsParent = 1 << 0,
    Pinned = 1 << 1,
    PathDoesNotExist = 1 << 2
}