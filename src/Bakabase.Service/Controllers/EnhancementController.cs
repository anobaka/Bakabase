using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
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
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Humanizer.Localisation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/enhancement")]
    public class EnhancementController(
        IResourceService resourceService,
        IEnhancementService enhancementService,
        IEnhancerService enhancerService,
        IEnhancerDescriptors enhancerDescriptors,
        IEnhancementRecordService enhancementRecordService,
        IResourceProfileService resourceProfileService,
        IMediaLibraryResourceMappingService mediaLibraryResourceMappingService)
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

            var enhancements = await enhancementService.GetAll(x => x.ResourceId == resourceId, additionalItem);

            var enhancementRecords =
                (await enhancementRecordService.GetAll(x => x.ResourceId == resourceId)).ToDictionary(
                    d => d.EnhancerId, d => d);

            // Use ResourceProfileService to get enhancer options for this resource
            var enhancerOptions = await resourceProfileService.GetEffectiveEnhancerOptions(resource);
            var enhancerOptionsSet = enhancerOptions.Select(e => e.EnhancerId).ToHashSet();

            // Build response for enhancers from resource profile
            var res = enhancerOptionsSet.Select(enhancerId =>
            {
                var ed = enhancerDescriptors[enhancerId];
                var es = enhancements.Where(e => e.EnhancerId == ed.Id).ToList();
                var record = enhancementRecords.GetValueOrDefault(enhancerId);
                var re = new ResourceEnhancements
                {
                    Enhancer = ed,
                    ContextCreatedAt = record?.ContextCreatedAt,
                    ContextAppliedAt = record?.ContextAppliedAt,
                    Status = record?.Status ?? default,
                    Logs = record?.Logs?.Select(l => new ResourceEnhancements.EnhancementLogViewModel
                    {
                        Timestamp = l.Timestamp,
                        Level = l.Level,
                        Event = l.Event,
                        Message = l.Message,
                        Data = l.Data
                    }).ToList(),
                    OptionsSnapshot = record?.OptionsSnapshot,
                    ErrorMessage = record?.ErrorMessage,
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

        [HttpDelete("~/media-library/{mediaLibraryId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteByEnhancementsMediaLibrary")]
        public async Task<BaseResponse> DeleteMediaLibraryEnhancementRecords(int mediaLibraryId, bool deleteEmptyOnly)
        {
            var resourceIds = (await mediaLibraryResourceMappingService.GetResourceIdsByMediaLibraryId(mediaLibraryId))
                .ToArray();

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

        [HttpDelete("~/media-library/{mediaLibraryId:int}/enhancer/{enhancerId:int}/enhancements")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByMediaLibraryAndEnhancer")]
        public async Task<BaseResponse> DeleteEnhancementRecordsByMediaLibraryAndEnhancer(int mediaLibraryId,
            int enhancerId,
            bool deleteEmptyOnly)
        {
            var resourceIds = (await mediaLibraryResourceMappingService.GetResourceIdsByMediaLibraryId(mediaLibraryId))
                .ToArray();
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

        [HttpDelete("~/resource-profile/{profileId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByResourceProfile")]
        public async Task<BaseResponse> DeleteResourceProfileEnhancementRecords(int profileId, bool deleteEmptyOnly)
        {
            var resourceIds = (await resourceProfileService.GetMatchingResourceIds(profileId)).ToArray();

            if (resourceIds.Length == 0)
            {
                return BaseResponseBuilder.Ok;
            }

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

        [HttpDelete("~/resource-profile/{profileId:int}/enhancer/{enhancerId:int}/enhancement")]
        [SwaggerOperation(OperationId = "DeleteEnhancementsByResourceProfileAndEnhancer")]
        public async Task<BaseResponse> DeleteEnhancementRecordsByResourceProfileAndEnhancer(int profileId,
            int enhancerId,
            bool deleteEmptyOnly)
        {
            var resourceIds = (await resourceProfileService.GetMatchingResourceIds(profileId)).ToArray();

            if (resourceIds.Length == 0)
            {
                return BaseResponseBuilder.Ok;
            }

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
    }
}