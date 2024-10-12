using System;

namespace Bakabase.InsideWorld.Models.Constants;

[Flags]
public enum ResourceDisplayContent
{
    MediaLibrary = 1 << 0,
    Category = 1 << 1,
    Tags = 1 << 2,

    All = MediaLibrary | Category | Tags
}