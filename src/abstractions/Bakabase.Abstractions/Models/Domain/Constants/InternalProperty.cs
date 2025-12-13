using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum InternalProperty
{
    RootPath = 1,
    ParentResource = 2,
    Resource = 3,
    Filename = 15,
    DirectoryPath = 16,
    CreatedAt = 17,
    FileCreatedAt = 18,
    FileModifiedAt = 19,
    [Obsolete]
    Category = 20,
    [Obsolete]
    MediaLibrary = 21,
    [Obsolete]
    MediaLibraryV2 = ResourceProperty.MediaLibraryV2,
    MediaLibraryV2Multi = ResourceProperty.MediaLibraryV2Multi,
    PlayedAt = ResourceProperty.PlayedAt
}