using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyResourceTagMappingService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<BakabaseDbContext,
            ResourceTagMapping, int>(serviceProvider)
    {
        // /// <summary>
        // /// Replace all tag mappings
        // /// </summary>
        // /// <param name="resourceTagMappings"></param>
        // /// <returns></returns>
        // public async Task<BaseResponse> PutRange(Dictionary<int, int[]?> resourceTagMappings)
        // {
        //     var resourceIds = resourceTagMappings.Keys.Distinct().ToArray();
        //     var exists = (await GetAll(a => resourceIds.Contains(a.ResourceId))).ToList();
        //     var uniqueMappings = exists.GroupBy(a => a).Select(a => a.Key.Id).ToList();
        //     var invalidExists = exists.Where(a => !uniqueMappings.Contains(a.Id)).ToList();
        //     var toBeRemoved = exists.Where(a => resourceTagMappings[a.ResourceId]?.Contains(a.TagId) != true)
        //         .Concat(invalidExists).ToArray();
        //     await RemoveRange(toBeRemoved);
        //     var allMappings = resourceTagMappings.Where(a => a.Value != null)
        //         .SelectMany(a => a.Value.Select(b => new ResourceTagMapping { TagId = b, ResourceId = a.Key })).ToList();
        //     var toBeAdded = allMappings.Where(r => !exists.Contains(r)).ToList();
        //     _ = (await base.AddRange(toBeAdded)).Data;
        //     return BaseResponseBuilder.Ok;
        // }
        //
        // public async Task RemoveDuplicate()
        // {
        //     try
        //     {
        //         // Check exists
        //         var count = await Count();
        //     }
        //     catch (Exception e)
        //     {
        //         Logger.LogWarning(
        //             $"An error occurred before removing duplicate resource-tag-mappings. {e.BuildFullInformationText()}");
        //         return;
        //     }
        //
        //     var entities = await GetAll();
        //     var groups = entities.GroupBy(t => t);
        //     var duplicateMappings = new List<ResourceTagMapping>();
        //     foreach (var g in groups)
        //     {
        //         if (g.Count() > 1)
        //         {
        //             duplicateMappings.AddRange(g.Skip(1));
        //         }
        //     }
        //
        //     if (duplicateMappings.Any())
        //     {
        //         await RemoveRange(duplicateMappings);
        //     }
        // }
    }
}