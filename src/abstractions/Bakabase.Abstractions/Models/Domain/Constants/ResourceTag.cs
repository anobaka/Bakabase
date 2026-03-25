namespace Bakabase.Abstractions.Models.Domain.Constants;

[Flags]
public enum ResourceTag
{
    IsParent = 1 << 0,
    Pinned = 1 << 1
}