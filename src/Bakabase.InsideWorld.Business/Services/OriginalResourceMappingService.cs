using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class OriginalResourceMappingService : FullMemoryCacheResourceService<
        InsideWorldDbContext, OriginalResourceMapping, int>
    {
        protected OriginalService OriginalService => GetRequiredService<OriginalService>();

        public OriginalResourceMappingService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override async Task<ListResponse<OriginalResourceMapping>> AddRange(
            List<OriginalResourceMapping> resources)
        {
            var distinctResources = resources.Distinct().ToList();
            var exists = await GetAll(a => distinctResources.Any(b => b.Equals(a)));
            var toBeAdded = distinctResources.Where(r => !exists.Contains(r)).ToList();
            var @new = (await base.AddRange(toBeAdded)).Data;
            return new(exists.Concat(@new));
        }

        public async Task UpdateResourceOriginals(int resourceId, string[] originalNames)
        {
            var originals = (await OriginalService.AddRange(originalNames.ToList())).Data;
            var originalIds = originals.Values.Select(t => t.Id).Distinct().ToHashSet();
            var currentMappings = await GetAll(t => t.ResourceId == resourceId);
            var toBeRemoved = currentMappings.Where(t => !originalIds.Contains(t.OriginalId)).ToArray();
            var currentOriginalIds = currentMappings.Select(t => t.OriginalId).Distinct().ToList();
            var toBeAdded = originalIds.Where(t => !currentOriginalIds.Contains(t)).Select(t =>
                new OriginalResourceMapping
                {
                    OriginalId = t,
                    ResourceId = resourceId
                });
            await RemoveRange(toBeRemoved);
            var @new = await AddRange(toBeAdded.ToList());
        }
    }
}