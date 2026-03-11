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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

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

                        // Also populate legacy PlayableFilePaths for backward compatibility
                        if (model.PlayableFilePaths.IsNotEmpty())
                        {
                            rc.PlayableFilePaths =
                                model.PlayableFilePaths.DeserializeAsStandardValue<List<string>>(StandardValueType
                                    .ListString);
                            rc.HasMorePlayableFiles = model.HasMorePlayableFiles;
                        }

                        // If PlayableItems is empty but PlayableFilePaths exists, build PlayableItems from legacy data
                        if (rc.PlayableItems == null && rc.PlayableFilePaths is { Count: > 0 })
                        {
                            rc.PlayableItems = rc.PlayableFilePaths
                                .Select(p => new PlayableItem
                                {
                                    Source = ResourceSource.FileSystem,
                                    Key = p
                                })
                                .ToList();
                        }

                        // Populate PlayableFilePaths from PlayableItems if not already set
                        if (rc.PlayableFilePaths == null && rc.PlayableItems is { Count: > 0 })
                        {
                            rc.PlayableFilePaths = rc.PlayableItems
                                .Where(i => i.Source == ResourceSource.FileSystem)
                                .Select(i => i.Key)
                                .ToList();
                            if (rc.PlayableFilePaths.Count == 0)
                                rc.PlayableFilePaths = null;
                        }

                        rc.HasMorePlayableFiles = model.HasMorePlayableFiles;

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
