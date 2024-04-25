using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
    public enum ResourceProperty
    {
        RootPath = 1,
        ParentResource,
        Resource,
        [Obsolete] ReleaseDt,
        [Obsolete] Publisher,
        [Obsolete] Name,
        [Obsolete] Language,
        [Obsolete] Volume,
        [Obsolete] Original,
        [Obsolete] Series,
        [Obsolete] Tag,
        [Obsolete] Introduction,
        [Obsolete] Rate,
        CustomProperty,
        FileName,
        DirectoryPath,
        CreatedAt,
        FileCreatedAt,
        FileModifiedAt,
        Category,
        MediaLibrary,
        [Obsolete] Favorites
    }
}