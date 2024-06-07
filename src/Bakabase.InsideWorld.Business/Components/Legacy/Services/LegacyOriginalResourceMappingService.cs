using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyOriginalResourceMappingService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<
            InsideWorldDbContext, OriginalResourceMapping, int>(serviceProvider)
    {
        // public override async Task<ListResponse<OriginalResourceMapping>> AddRange(
        //     List<OriginalResourceMapping> resources)
        // {
        //     var distinctResources = resources.Distinct().ToList();
        //     var exists = await GetAll(a => distinctResources.Any(b => b.Equals(a)));
        //     var toBeAdded = distinctResources.Where(r => !exists.Contains(r)).ToList();
        //     var @new = (await base.AddRange(toBeAdded)).Data;
        //     return new(exists.Concat(@new));
        // }
        //
        // public async Task UpdateResourceOriginals(int resourceId, string[] originalNames)
        // {
        //     var originals = (await OriginalService.GetOrAddRangeByNames(originalNames.ToList())).Data;
        //     var originalIds = originals.Values.Select(t => t.Id).Distinct().ToHashSet();
        //     var currentMappings = await GetAll(t => t.ResourceId == resourceId);
        //     var toBeRemoved = currentMappings.Where(t => !originalIds.Contains(t.OriginalId)).ToArray();
        //     var currentOriginalIds = currentMappings.Select(t => t.OriginalId).Distinct().ToList();
        //     var toBeAdded = originalIds.Where(t => !currentOriginalIds.Contains(t)).Select(t =>
        //         new OriginalResourceMapping
        //         {
        //             OriginalId = t,
        //             ResourceId = resourceId
        //         });
        //     await RemoveRange(toBeRemoved);
        //     var @new = await AddRange(toBeAdded.ToList());
        // }
        //
        // public async Task PutRange(Dictionary<int, List<OriginalDto>?>? map)
        // {
        //     if (map == null)
        //     {
        //         return;
        //     }
        //
        //     var resourceIds = map.Keys.ToArray();
        //     var existedMappingsMap = (await GetAll(x => resourceIds.Contains(x.ResourceId))).GroupBy(x => x.ResourceId)
        //         .ToDictionary(x => x.Key, x => x.ToList());
        //
        //     var toBeDeleted = new List<OriginalResourceMapping>();
        //     var toBeAdded = new List<OriginalResourceMapping>();
        //     foreach (var (rId, originals) in map)
        //     {
        //         var mappings = originals?.Select(x => new OriginalResourceMapping {OriginalId = x.Id, ResourceId = rId})
        //             .ToList();
        //         if (mappings?.Any() == true)
        //         {
        //             var existedMappings = existedMappingsMap.GetValueOrDefault(rId);
        //             if (existedMappings != null)
        //             {
        //                 var @new = mappings.Except(existedMappings).ToList();
        //                 var bad = existedMappings.Except(mappings);
        //                 toBeAdded.AddRange(@new);
        //                 toBeDeleted.AddRange(bad);
        //             }
        //             else
        //             {
        //                 toBeAdded.AddRange(mappings);
        //             }
        //         }
        //         else
        //         {
        //             if (existedMappingsMap.TryGetValue(rId, out var existedMappings))
        //             {
        //                 toBeDeleted.AddRange(existedMappings);
        //             }
        //         }
        //     }
        //
        //     await RemoveRange(toBeDeleted);
        //     await AddRange(toBeAdded);
        // }
    }
}