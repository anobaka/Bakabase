﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using Humanizer.Localisation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/enhancement")]
    public class EnhancementController(
        ICategoryService categoryService,
        IResourceService resourceService,
        IEnhancementService enhancementService,
        IEnhancerService enhancerService,
        ICategoryEnhancerOptionsService categoryEnhancerOptionsService,
        IEnhancerDescriptors enhancerDescriptors,
        IEnhancementRecordService enhancementRecordService)
        : Controller
    {
        [HttpGet("~/resource/{resourceId:int}/enhancement")]
        [SwaggerOperation(OperationId = "GetResourceEnhancements")]
        public async Task<ListResponse<ResourceEnhancements>> GetResourceEnhancementRecords(int resourceId,
            EnhancementAdditionalItem additionalItem = EnhancementAdditionalItem.None)
        {
            var resource = await resourceService.Get(resourceId, ResourceAdditionalItem.None);
            if (resource == null)
            {
                return ListResponseBuilder<ResourceEnhancements>.NotFound;
            }

            var category = await categoryService.Get(resource.CategoryId, CategoryAdditionalItem.None);
            if (category == null)
            {
                return ListResponseBuilder<ResourceEnhancements>.NotFound;
            }

            var enhancements = await enhancementService.GetAll(x => x.ResourceId == resourceId, additionalItem);
            var categoryEnhancerOptions = await categoryEnhancerOptionsService.GetByCategory(resource.CategoryId);
            var enhancementRecords =
                (await enhancementRecordService.GetAll(x => x.ResourceId == resourceId)).ToDictionary(
                    d => d.EnhancerId, d => d);

            var res = categoryEnhancerOptions.Where(o => o.Active).Select(o =>
            {
                var ed = enhancerDescriptors[o.EnhancerId];
                var es = enhancements.Where(e => e.EnhancerId == ed.Id).ToList();
                var record = enhancementRecords.GetValueOrDefault(o.EnhancerId);
                var re = new ResourceEnhancements
                {
                    Enhancer = ed,
                    ContextCreatedAt = record?.ContextCreatedAt,
                    ContextAppliedAt = record?.ContextAppliedAt,
                    Status = record?.Status ?? default,
                    Targets = ed.Targets.Where(x => !x.IsDynamic).Select(t =>
                    {
                        var targetId = Convert.ToInt32(t.Id);
                        var e = es.FirstOrDefault(e => e.Target == targetId);
                        return new ResourceEnhancements.TargetEnhancement
                        {
                            Enhancement = e?.ToViewModel(),
                            Target = targetId,
                            TargetName = t.Name
                        };
                    }).ToArray(),
                    DynamicTargets = ed.Targets.Where(x => x.IsDynamic).Select(t =>
                    {
                        var targetId = Convert.ToInt32(t.Id);
                        var e = es.Where(e => e.Target == targetId).Select(e => e.ToViewModel()).ToList();
                        return new ResourceEnhancements.DynamicTargetEnhancements()
                        {
                            Enhancements = e,
                            Target = targetId,
                            TargetName = t.Name
                        };
                    }).ToArray()
                };
                return re;
            }).ToList();

            return new ListResponse<ResourceEnhancements>(res);
        }

        [HttpDelete("~/resource/{resourceId:int}/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteResourceEnhancement")]
        public async Task<BaseResponse> DeleteResourceEnhancementRecords(int resourceId, int enhancerId)
        {
            await enhancementService.RemoveAll(x => x.ResourceId == resourceId && x.EnhancerId == enhancerId, true);
            await enhancementRecordService.DeleteAll(t => resourceId == t.ResourceId && enhancerId == t.EnhancerId);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("~/resource/{resourceId:int}/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "EnhanceResourceByEnhancer")]
        public async Task<BaseResponse> EnhanceResourceByEnhancer(int resourceId, int enhancerId)
        {
            await DeleteResourceEnhancementRecords(resourceId, enhancerId);
            await enhancerService.EnhanceResource(resourceId, [enhancerId], PauseToken.None, CancellationToken.None);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("~/resource/{resourceId:int}/enhancer/{enhancerId:int}/enhancement/apply")]
        [SwaggerOperation(OperationId = "ApplyEnhancementContextDataForResourceByEnhancer")]
        public async Task<BaseResponse> ApplyEnhancementContextDataForResourceByEnhancer(int resourceId, int enhancerId)
        {
            await enhancerService.ReapplyEnhancementsByResources([resourceId], [enhancerId], CancellationToken.None);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("~/category/{categoryId:int}/enhancer/{enhancerId:int}/enhancement/apply")]
        [SwaggerOperation(OperationId = "ApplyEnhancementContextDataByEnhancerAndCategory")]
        public async Task<BaseResponse> ApplyEnhancementContextDataByEnhancerAndCategory(int categoryId, int enhancerId)
        {
            await enhancerService.ReapplyEnhancementsByCategory(categoryId, enhancerId, CancellationToken.None);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/media-library/{mediaLibraryId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteByEnhancementsMediaLibrary")]
        public async Task<BaseResponse> DeleteMediaLibraryEnhancementRecords(int mediaLibraryId, bool deleteEmptyOnly)
        {
            var resourceIds = (await resourceService.GetAll(t => t.MediaLibraryId == mediaLibraryId))
                .Select(t => t.Id).ToArray();

            if (deleteEmptyOnly)
            {
                var records = await enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId));
                var enhancements = await enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId));
                var resourceIdEnhancerIdMap = enhancements.GroupBy(x => x.ResourceId)
                    .ToDictionary(d => d.Key, d => d.Select(x => x.EnhancerId).ToHashSet());
                var recordIdsWithEmptyEnhancements =
                    records.Where(x =>
                        !resourceIdEnhancerIdMap.TryGetValue(x.ResourceId, out var enhancerIds) ||
                        !enhancerIds.Contains(x.EnhancerId)).Select(x => x.Id).ToList();
                await enhancementRecordService.DeleteAll(r => recordIdsWithEmptyEnhancements.Contains(r.Id));
            }
            else
            {
                await enhancementService.RemoveAll(t => resourceIds.Contains(t.ResourceId), true);
                await enhancementRecordService.DeleteAll(t => resourceIds.Contains(t.ResourceId));
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/category/{categoryId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByCategory")]
        public async Task<BaseResponse> DeleteCategoryEnhancementRecords(int categoryId, bool deleteEmptyOnly)
        {
            var resourceIds = (await resourceService.GetAll(t => t.CategoryId == categoryId))
                .Select(t => t.Id).ToArray();
            if (deleteEmptyOnly)
            {
                var records = await enhancementRecordService.GetAll(x => resourceIds.Contains(x.ResourceId));
                var enhancements = await enhancementService.GetAll(x => resourceIds.Contains(x.ResourceId));
                var resourceIdEnhancerIdMap = enhancements.GroupBy(x => x.ResourceId)
                    .ToDictionary(d => d.Key, d => d.Select(x => x.EnhancerId).ToHashSet());
                var recordIdsWithEmptyEnhancements =
                    records.Where(x =>
                        !resourceIdEnhancerIdMap.TryGetValue(x.ResourceId, out var enhancerIds) ||
                        !enhancerIds.Contains(x.EnhancerId)).Select(x => x.Id).ToList();
                await enhancementRecordService.DeleteAll(r => recordIdsWithEmptyEnhancements.Contains(r.Id));
            }
            else
            {
                await enhancementService.RemoveAll(t => resourceIds.Contains(t.ResourceId), true);
                await enhancementRecordService.DeleteAll(t => resourceIds.Contains(t.ResourceId));
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/category/{categoryId:int}/enhancer/{enhancerId:int}/enhancements")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByCategoryAndEnhancer")]
        public async Task<BaseResponse> DeleteEnhancementRecordsByCategoryAndEnhancer(int categoryId, int enhancerId,
            bool deleteEmptyOnly)
        {
            var resourceIds = (await resourceService.GetAll(t => t.CategoryId == categoryId))
                .Select(t => t.Id).ToArray();
            if (deleteEmptyOnly)
            {
                var records = await enhancementRecordService.GetAll(x =>
                    x.EnhancerId == enhancerId && resourceIds.Contains(x.ResourceId));
                var enhancements = await enhancementService.GetAll(x =>
                    x.EnhancerId == enhancerId && resourceIds.Contains(x.ResourceId));
                var resourceIdEnhancerIdMap = enhancements.GroupBy(x => x.ResourceId)
                    .ToDictionary(d => d.Key, d => d.Select(x => x.EnhancerId).ToHashSet());
                var recordIdsWithEmptyEnhancements =
                    records.Where(x =>
                        !resourceIdEnhancerIdMap.TryGetValue(x.ResourceId, out var enhancerIds) ||
                        !enhancerIds.Contains(x.EnhancerId)).Select(x => x.Id).ToList();
                await enhancementRecordService.DeleteAll(r => recordIdsWithEmptyEnhancements.Contains(r.Id));
            }
            else
            {
                await enhancementService.RemoveAll(
                    t => t.EnhancerId == enhancerId && resourceIds.Contains(t.ResourceId), true);
                await enhancementRecordService.DeleteAll(t =>
                    t.EnhancerId == enhancerId && resourceIds.Contains(t.ResourceId));
            }

            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByEnhancer")]
        public async Task<BaseResponse> DeleteEnhancerEnhancementRecords(int enhancerId, bool deleteEmptyOnly)
        {
            if (deleteEmptyOnly)
            {
                var records = await enhancementRecordService.GetAll(x => enhancerId == x.EnhancerId);
                var enhancements = await enhancementService.GetAll(x => x.EnhancerId == enhancerId);
                var resourceIdsWithEnhancements = enhancements.Select(x => x.ResourceId).ToHashSet();
                var recordIdsWithEmptyEnhancements =
                    records.Where(x => !resourceIdsWithEnhancements.Contains(x.ResourceId)).Select(x => x.Id).ToList();
                await enhancementRecordService.DeleteAll(r => recordIdsWithEmptyEnhancements.Contains(r.Id));
            }
            else
            {
                await enhancementService.RemoveAll(t => t.EnhancerId == enhancerId, true);
                await enhancementRecordService.DeleteAll(t => t.EnhancerId == enhancerId);
            }

            return BaseResponseBuilder.Ok;
        }
    }
}