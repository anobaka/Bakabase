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
        ParentResource = 2,
        Resource = 3,
        Introduction = 12,
        Rating = 13,
        CustomProperty = 14,
        Filename = 15,
        DirectoryPath = 16,
        CreatedAt = 17,
        FileCreatedAt = 18,
        FileModifiedAt = 19,
        [Obsolete]
        Category = 20,
        [Obsolete]
        MediaLibrary = 21,
        Cover = 22,
        PlayedAt = 23,
        /// <summary>
        /// DEPRECATED: Use MediaLibraryV2Multi instead.
        /// This is a facade - all operations redirect to MediaLibraryV2Multi internally.
        /// Returns SingleChoice format (first value only) for backward compatibility.
        /// </summary>
        [Obsolete("Use MediaLibraryV2Multi instead. Facade only - all operations redirect to MediaLibraryV2Multi.")]
        MediaLibraryV2 = 24,
        /// <summary>
        /// Media library binding with multiple choice support.
        /// New code should use this instead of MediaLibraryV2.
        /// </summary>
        MediaLibraryV2Multi = 25,
    }
}