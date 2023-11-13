using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Services
{
    public class BulkModificationService : ResourceService<InsideWorldDbContext, BulkModification, int>
    {
        /// <summary>
        /// Bulk id - BulkModificationFilterResult
        /// </summary>
        private static readonly MemoryCache Cache;

        static BulkModificationService()
        {
            var config = new NameValueCollection
            {
                {"physicalMemoryLimitPercentage", "10"},
                {"cacheMemoryLimitMegabytes", "2000"}
            };
            Cache = new MemoryCache(nameof(BulkModification), config);
        }

        protected ResourceService ResourceService => GetRequiredService<ResourceService>();

        public BulkModificationService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<BulkModificationDto> GetDto(int id)
        {
            var d = await GetByKey(id);
            return d.ToDto();
        }


        public async Task<List<ResourceDto>> Filter(int id)
        {
            var cacheKey = id.ToString();

            var bulkModification = await GetDto(id);
            var rootFilter = bulkModification.Filter;

            if (Cache.Get(cacheKey) is BulkModificationFilterResult cache)
            {
                if (cache.FilterKey == rootFilter?.ToString())
                {
                    return (cache.Resources);
                }

                Cache.Remove(cacheKey);
            }

            if (rootFilter != null)
            {
                var allResources = await ResourceService.GetAll(ResourceAdditionalItem.All);
                var exp = rootFilter.BuildExpression();
                var filteredResources = allResources.Where(exp.Compile()).ToList();
                Cache.Add(cacheKey,
                    new BulkModificationFilterResult
                        {CreatedAt = DateTime.Now, FilterKey = rootFilter.ToString(), Resources = filteredResources},
                    new CacheItemPolicy {SlidingExpiration = TimeSpan.FromMinutes(20)});
                return (filteredResources);
            }

            return new List<ResourceDto>();
        }

        public async Task<Dictionary<ResourceDto, List<ResourceDiff>>> Preview(int id)
        {
            var data = await GetDto(id);
            var resources = await Filter(id);
            var diffs = new Dictionary<ResourceDto, List<ResourceDiff>>();

            // todo:

            return diffs;
        }
    }
}