using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/enhancement")]
    public class EnhancementController : Controller
    {
        private readonly IEnhancementService _enhancementService;
        private readonly IEnhancerService _enhancerService;
        private readonly MediaLibraryService _mediaLibraryService;
        private readonly ResourceCategoryService _categoryService;
        private readonly ResourceService _resourceService;
        private readonly ICategoryEnhancerOptionsService _categoryEnhancerOptionsService;
        private readonly IEnumerable<EnhancerDescriptor> _enhancerDescriptors;

        public EnhancementController(MediaLibraryService mediaLibraryService, ResourceCategoryService categoryService,
            ResourceService resourceService, IEnhancementService enhancementService, IEnhancerService enhancerService, ICategoryEnhancerOptionsService categoryEnhancerOptionsService, IEnumerable<EnhancerDescriptor> enhancerDescriptors)
        {
            _mediaLibraryService = mediaLibraryService;
            _categoryService = categoryService;
            _resourceService = resourceService;
            _enhancementService = enhancementService;
            _enhancerService = enhancerService;
            _categoryEnhancerOptionsService = categoryEnhancerOptionsService;
            _enhancerDescriptors = enhancerDescriptors;
        }

        [HttpGet("~/resource/{resourceId:int}/enhancement")]
        [SwaggerOperation(OperationId = "GetResourceEnhancements")]
        public async Task<ListResponse<ResourceEnhancements>> GetResourceEnhancementRecords(int resourceId, EnhancementAdditionalItem additionalItem = EnhancementAdditionalItem.None)
        {
            var resource = await _resourceService.GetByKey(resourceId, false);
            if (resource == null)
            {
                return ListResponseBuilder<ResourceEnhancements>.NotFound;
            }

            var category = await _categoryService.GetByKey(resource.CategoryId, false);
            if (category == null)
            {
                return ListResponseBuilder<ResourceEnhancements>.NotFound;
            }

            var enhancements = await _enhancementService.GetAll(x => x.ResourceId == resourceId, additionalItem);
            var categoryEnhancerOptions = await _categoryEnhancerOptionsService.GetByCategory(resource.CategoryId);

            var edMap = _enhancerDescriptors.ToDictionary(d => d.Id, d => d);
            var res = categoryEnhancerOptions.Where(o => o.Active).Select(o =>
            {
                var ed = edMap.GetValueOrDefault(o.EnhancerId);
                var es = enhancements.Where(e => e.EnhancerId == o.Id).ToList();
                var re = new ResourceEnhancements
                {
                    EnhancerId = o.EnhancerId,
                    EnhancerName = ed?.Name ?? o.EnhancerId.ToString(),
                    EnhancedAt = es.FirstOrDefault()?.CreatedAt,
                    Targets = ed?.Targets.Select(t =>
                    {
                        var targetId = Convert.ToInt32(t.Id);
                        var e = es.FirstOrDefault(e => e.Target == targetId);
                        return new ResourceEnhancements.TargetEnhancement
                        {
                            Enhancement = e,
                            Target = targetId,
                            TargetName = t.Name
                        };
                    }).ToArray() ?? []
                };
                return re;
            }).ToList();

            return new ListResponse<ResourceEnhancements>(res);
        }

        [HttpDelete("~/resource/{resourceId:int}/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteResourceEnhancement")]
        public async Task<BaseResponse> RemoveResourceEnhancementRecords(int resourceId, int enhancerId)
        {
            return await _enhancementService.RemoveAll(x => x.ResourceId == resourceId && x.EnhancerId == enhancerId,
                true);
        }

        [HttpPost("~/resource/{resourceId:int}/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "CreateEnhancementForResourceByEnhancer")]
        public async Task<BaseResponse> CreateEnhancementForResourceByEnhancer(int resourceId, int enhancerId)
        {
            await _enhancementService.RemoveAll(x => x.EnhancerId == enhancerId && x.ResourceId == resourceId, true);
            await _enhancerService.EnhanceResource(resourceId, [enhancerId]);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/media-library/{mediaLibraryId:int}/enhancement")]
        [SwaggerOperation(OperationId = "RemoveMediaLibraryEnhancements")]
        public async Task<BaseResponse> RemoveMediaLibraryEnhancementRecords(int mediaLibraryId)
        {
            var resourceIds = (await _resourceService.GetAllEntities(t => t.MediaLibraryId == mediaLibraryId))
                .Select(t => t.Id).ToArray();
            return await _enhancementService.RemoveAll(t => resourceIds.Contains(t.Id), true);
        }

        [HttpDelete("~/category/{categoryId:int}/enhancement")]
        [SwaggerOperation(OperationId = "RemoveCategoryEnhancements")]
        public async Task<BaseResponse> RemoveCategoryEnhancementRecords(int categoryId)
        {
            var resourceIds = (await _resourceService.GetAllEntities(t => t.CategoryId == categoryId))
                .Select(t => t.Id).ToArray();
            return await _enhancementService.RemoveAll(t => resourceIds.Contains(t.Id), true);
        }
    }
}