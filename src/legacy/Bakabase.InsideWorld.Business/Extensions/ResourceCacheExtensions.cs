using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Extensions;

public static class ResourceCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

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
                        // Deserialize PlayableItems (new multi-source format)
                        if (model.PlayableItems.IsNotEmpty())
                        {
                            try
                            {
                                rc.PlayableItems = JsonSerializer.Deserialize<List<PlayableItem>>(model.PlayableItems!, JsonOptions);
                            }
                            catch
                            {
                                rc.PlayableItems = null;
                            }
                        }
                        
                        rc.HasMoreFileSystemPlayableItems = model.HasMoreFileSystemPlayableItems;

                        break;
                    }
                    case ResourceCacheType.ResourceMarkers:
                    {
                        // No additional data to deserialize for ResourceMarkers
                        // The presence of this flag in CachedTypes indicates the marker file was created
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
