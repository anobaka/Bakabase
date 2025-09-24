namespace Bakabase.Abstractions.Models.Domain.Constants;

[Flags]
public enum ResourceCacheType
{
    Covers = 1,
    PlayableFiles = 2,
    ResourceMarkers = 4
}