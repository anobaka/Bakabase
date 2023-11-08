using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Humanizer.Localisation;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("bulk-modification")]
    public class BulkModificationController : Controller
    {
        static BulkModificationController()
        {
            var config = new NameValueCollection
            {
                {"physicalMemoryLimitPercentage", "10"},
                {"cacheMemoryLimitMegabytes", "2000"}
            };
            Cache = new MemoryCache(nameof(BulkModification), config);

        }

        /// <summary>
        /// Bulk id - BulkModificationFilterResult
        /// </summary>
        private static readonly MemoryCache Cache;

        private readonly BulkModificationService _service;
        private readonly ResourceService _resourceService;

        public BulkModificationController(BulkModificationService service, ResourceService resourceService)
        {
            _service = service;
            _resourceService = resourceService;
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "CreateBulkModification")]
        public async Task<SingletonResponse<BulkModificationDto>> Add([FromBody] BulkModificationPutRequestModel model)
        {
            var data = await _service.Add(new BulkModification
            {
                Name = model.Name,
                Filter = JsonConvert.SerializeObject(model.Filter),
                Modification = JsonConvert.SerializeObject(model.Modification),
            });
            return new SingletonResponse<BulkModificationDto>(data.Data.ToDto());
        }

        [HttpPut]
        [SwaggerOperation(OperationId = "PutBulkModification")]
        public async Task<BaseResponse> Put(int id, [FromBody] BulkModificationPutRequestModel model)
        {
            return await _service.UpdateByKey(id, d =>
            {
                d.Name = model.Name;
                d.Filter = JsonConvert.SerializeObject(model.Filter);
                d.Modification = JsonConvert.SerializeObject(model.Modification);
            });
        }

        [HttpDelete]
        [SwaggerOperation(OperationId = "RemoveBulkModification")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpGet("{id}/filtered-resources")]
        [SwaggerOperation(OperationId = "GetBulkModificationFilteredResources")]
        public async Task<ListResponse<ResourceDto>> Filter(int id)
        {
            var cacheKey = id.ToString();

            var bulkModification = await _service.GetDto(id);
            var rootFilter = bulkModification.Filter;

            if (Cache.Get(cacheKey) is BulkModificationFilterResult cache)
            {
                if (cache.FilterKey == rootFilter?.ToString())
                {
                    return new ListResponse<ResourceDto>(cache.Resources);
                }

                Cache.Remove(cacheKey);
            }

            if (rootFilter != null)
            {
                var allResources = await _resourceService.GetAll(ResourceAdditionalItem.All);
                var exp = rootFilter.BuildExpression();
                var filteredResources = allResources.Where(exp.Compile()).ToList();
                Cache.Add(cacheKey,
                    new BulkModificationFilterResult
                        {CreatedAt = DateTime.Now, FilterKey = rootFilter.ToString(), Resources = filteredResources},
                    new CacheItemPolicy {SlidingExpiration = TimeSpan.FromMinutes(20)});
                return new ListResponse<ResourceDto>(filteredResources);
            }

            return new ListResponse<ResourceDto>();
        }

        [HttpPost("{id}/diff")]
        [SwaggerOperation(OperationId = "CreateBulkModificationDiffs")]
        public async Task<BaseResponse> CreateDiffs(int id)
        {
            var data = await _service.GetDto(id);

            var resources = await Filter(id);

            foreach (var process in data.Modifications)
            {
                
            }
        }
    }
}