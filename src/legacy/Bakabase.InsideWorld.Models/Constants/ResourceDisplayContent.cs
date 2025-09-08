using System;

namespace Bakabase.InsideWorld.Models.Constants;

[Flags]
public enum ResourceDisplayContent
{
    MediaLibrary = 1 << 0,
    Category = 1 << 1,
    Tags = 1 << 2,
    AddedDate = 1 << 3,
    UpdatedDate = 1 << 4,
    FileCreatedDate = 1 << 5,
    FileModifiedDate = 1 << 6,

    Default = MediaLibrary | Category | Tags | FileCreatedDate,
}