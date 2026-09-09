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
        // [Obsolete]
        // Category = 20,
        // [Obsolete]
        // MediaLibrary = 21,
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
        /// <summary>
        /// Resource source (e.g. FileSystem, Steam, DLsite, ExHentai).
        /// MultipleChoice - a resource can be linked to multiple sources.
        /// </summary>
        Source = 26,
        /// <summary>
        /// Resource name. Reserved property auto-populated during sync:
        /// - PathMark: filename without extension
        /// - External sources (Steam, DLsite, ExHentai): display name from resolver
        /// </summary>
        Name = 27,
        /// <summary>
        /// Aggregated health score (Number). Computed by the HealthScore module from
        /// matching ScoringProfiles; null when no profile has scored the resource.
        /// </summary>
        HealthScore = 28,
        /// <summary>
        /// Which collections the resource belongs to (MultipleChoice). Derived from the
        /// collection memberships — written-down ones and rule matches alike — so that
        /// "everything in this series" is an ordinary resource search rather than a
        /// separate way of listing things.
        /// </summary>
        CollectionMulti = 29,
        /// <summary>
        /// Whether the resource currently has local files (Boolean). Derived from
        /// <c>Resource.Path</c>: a resource without a path is known to Bakabase but not
        /// materialized on disk yet (an uninstalled Steam game, a work the user intends
        /// to acquire). Read-only; set by materializing or dematerializing the resource.
        /// </summary>
        HasLocalPath = 30,
    }
}