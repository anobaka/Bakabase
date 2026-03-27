using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class ResourceCacheExtensions
{
    public static ResourceFileSystemCache ToDomainModel(this ResourceCacheDbModel model)
    {
        var rc = new ResourceFileSystemCache
        {
            CachedTypes = SpecificEnumUtils<ResourceCacheType>.Values
                .Where(x => model.CachedTypes.HasFlag(x))
                .ToList()
        };

        if (model.CachedTypes.HasFlag(ResourceCacheType.Covers))
        {
            if (model.CoverPaths.IsNotEmpty())
            {
                rc.CoverPaths =
                    model.CoverPaths.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
            }
        }

        if (model.CachedTypes.HasFlag(ResourceCacheType.PlayableFiles))
        {
            if (model.PlayableFilePaths.IsNotEmpty())
            {
                rc.PlayableFilePaths =
                    model.PlayableFilePaths.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
            }
        }

        return rc;
    }
}
