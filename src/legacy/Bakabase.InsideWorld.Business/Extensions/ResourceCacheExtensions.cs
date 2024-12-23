using System;
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
    public static ResourceCache ToDomainModel(this ResourceCacheDbModel model)
    {
        var rc = new ResourceCache
        {
            CachedTypes = SpecificEnumUtils<ResourceCacheType>.Values.Where(x => model.CachedTypes.HasFlag(x))
                .Aggregate(new List<ResourceCacheType>(),
                    (s, t) =>
                    {
                        s.Add(t);
                        return s;
                    })
        };
        foreach (var ct in SpecificEnumUtils<ResourceCacheType>.Values)
        {
            if (model.CachedTypes.HasFlag(ct))
            {
                switch (ct)
                {
                    case ResourceCacheType.Covers:
                    {
                        if (model.CoverPaths.IsNotEmpty())
                        {
                            rc.CoverPaths =
                                model.CoverPaths.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
                        }

                        break;
                    }
                    case ResourceCacheType.PlayableFiles:
                    {
                        if (model.PlayableFilePaths.IsNotEmpty())
                        {
                            rc.PlayableFilePaths =
                                model.PlayableFilePaths.DeserializeAsStandardValue<List<string>>(StandardValueType
                                    .ListString);
                            rc.HasMorePlayableFiles = model.HasMorePlayableFiles;
                        }

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        return rc;
    }
}