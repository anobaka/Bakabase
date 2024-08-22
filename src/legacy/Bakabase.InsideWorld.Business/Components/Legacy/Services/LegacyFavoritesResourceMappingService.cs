using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyFavoritesResourceMappingService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<
            InsideWorldDbContext, FavoritesResourceMapping, int>(serviceProvider)
    {
        // public override async Task<ListResponse<FavoritesResourceMapping>> AddRange(
        //     List<FavoritesResourceMapping> resources)
        // {
        //     var distinctResources = resources.Distinct().ToList();
        //     var exists = await GetAll(a => distinctResources.Any(b => b.Equals(a)));
        //     var toBeAdded = distinctResources.Where(r => !exists.Contains(r)).ToList();
        //     var @new = (await base.AddRange(toBeAdded)).Data;
        //     return new(exists.Concat(@new));
        // }
        //
        // public async Task<BaseResponse> UpdateResourcesFavoritesMappings(Dictionary<int, int[]> dict)
        // {
        //     var mappings = dict.SelectMany(t =>
        //         t.Value.Select(b => new FavoritesResourceMapping {ResourceId = t.Key, FavoritesId = b})).ToArray();
        //     var resourceIds = dict.Keys.ToArray();
        //     var currentMappings = await GetAll(t => resourceIds.Contains(t.ResourceId));
        //     var toBeRemoved = currentMappings.Except(mappings);
        //     var toBeAdded = mappings.Except(currentMappings);
        //     await RemoveRange(toBeRemoved);
        //     var @new = await AddRange(toBeAdded.ToList());
        //     return BaseResponseBuilder.Ok;
        // }
    }
}