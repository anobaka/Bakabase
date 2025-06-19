using System;

namespace Bakabase.InsideWorld.Models.Constants;

public enum SearchableReservedProperty
{
	FileName = ResourceProperty.Filename,
	DirectoryPath = ResourceProperty.DirectoryPath,
	CreatedAt = ResourceProperty.CreatedAt,
	FileCreatedAt = ResourceProperty.FileCreatedAt,
	FileModifiedAt = ResourceProperty.FileModifiedAt,
	[Obsolete]
	Category = ResourceProperty.Category,
    [Obsolete]
    MediaLibrary = ResourceProperty.MediaLibrary,
	Introduction = ResourceProperty.Introduction,
	Rating = ResourceProperty.Rating,
	Cover = ResourceProperty.Cover,
    MediaLibraryV2 = ResourceProperty.MediaLibraryV2
}