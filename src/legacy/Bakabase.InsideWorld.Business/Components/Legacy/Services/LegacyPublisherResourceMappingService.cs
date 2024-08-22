using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyPublisherResourceMappingService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<
            InsideWorldDbContext, PublisherResourceMapping, int>(serviceProvider)
    {
        // [Obsolete($"Use {nameof(PutRange)} instead")]
        // public override async Task<ListResponse<PublisherResourceMapping>> AddRange(
        //     List<PublisherResourceMapping> resources)
        // {
        //     var distinctResources = resources.Distinct().ToList();
        //     var exists = await GetAll(a => distinctResources.Any(b => b.Equals(a)));
        //     var toBeAdded = distinctResources.Where(r => !exists.Contains(r)).ToList();
        //     var @new = (await base.AddRange(toBeAdded)).Data;
        //     return new(exists.Concat(@new));
        // }
        //
        // [Obsolete($"Use {nameof(PutRange)} instead")]
        // public async Task UpdateResourcePublishers(int resourceId, PublisherDto[] newPublishers)
        // {
        //     var publishers = (await PublisherService.GetOrAddRangeByNames(newPublishers.ToList())).Data;
        //     newPublishers.PopulateId(publishers.ToDictionary(t => t.Key, t => t.Value.Id));
        //     var mappings = newPublishers.BuildMappings(resourceId);
        //
        //     var currentMappings = await GetAll(t => t.ResourceId == resourceId);
        //     var toBeRemoved = currentMappings.Except(mappings);
        //     var toBeAdded = mappings.Except(currentMappings);
        //     await RemoveRange(toBeRemoved);
        //     var @new = await AddRange(toBeAdded.ToList());
        // }
        //
        // public async Task PutRange(Dictionary<int, List<PublisherDto>?>? map)
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
        //     var toBeDeleted = new List<PublisherResourceMapping>();
        //     var toBeAdded = new List<PublisherResourceMapping>();
        //     foreach (var (rId, publishers) in map)
        //     {
        //         var mappings = publishers.BuildMappings(rId);
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