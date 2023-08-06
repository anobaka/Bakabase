using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PublisherResourceMappingService : FullMemoryCacheResourceService<
        InsideWorldDbContext, PublisherResourceMapping, int>
    {
        protected PublisherService PublisherService => GetRequiredService<PublisherService>();

        public PublisherResourceMappingService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public override async Task<ListResponse<PublisherResourceMapping>> AddRange(
            List<PublisherResourceMapping> resources)
        {
            var distinctResources = resources.Distinct().ToList();
            var exists = await GetAll(a => distinctResources.Any(b => b.Equals(a)));
            var toBeAdded = distinctResources.Where(r => !exists.Contains(r)).ToList();
            var @new = (await base.AddRange(toBeAdded)).Data;
            return new(exists.Concat(@new));
        }

        public async Task UpdateResourcePublishers(int resourceId, PublisherDto[] newPublishers)
        {
            var publishers = (await PublisherService.AddAll(newPublishers.ToList())).Data;
            newPublishers.PopulateId(publishers.ToDictionary(t => t.Key, t => t.Value.Id));
            var mappings = newPublishers.BuildMappings(resourceId);

            var currentMappings = await GetAll(t => t.ResourceId == resourceId);
            var toBeRemoved = currentMappings.Except(mappings);
            var toBeAdded = mappings.Except(currentMappings);
            await RemoveRange(toBeRemoved);
            var @new = await AddRange(toBeAdded.ToList());
        }
    }
}