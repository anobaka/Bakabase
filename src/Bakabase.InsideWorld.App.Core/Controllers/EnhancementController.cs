using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/enhancement")]
    public class EnhancementController : Controller
    {
        private readonly EnhancementRecordService _enhancementRecordService;
        private readonly MediaLibraryService _mediaLibraryService;
        private readonly ResourceCategoryService _categoryService;
        private readonly ResourceService _resourceService;

        public EnhancementController(EnhancementRecordService enhancementRecordService,
            MediaLibraryService mediaLibraryService, ResourceCategoryService categoryService,
            ResourceService resourceService)
        {
            _enhancementRecordService = enhancementRecordService;
            _mediaLibraryService = mediaLibraryService;
            _categoryService = categoryService;
            _resourceService = resourceService;
        }

        [HttpGet("~/resource/{id}/enhancement")]
        [SwaggerOperation(OperationId = "GetResourceEnhancementRecords")]
        public async Task<ListResponse<EnhancementRecordDto>> GetResourceEnhancementRecords(int id)
        {
            return new ListResponse<EnhancementRecordDto>(
                await _enhancementRecordService.GetAll(t => t.ResourceId == id));
        }

        [HttpPost("~/resource/{id}/enhancement")]
        [SwaggerOperation(OperationId = "EnhanceResource")]
        public async Task<BaseResponse> Enhance(int id)
        {
            await _enhancementRecordService.EnhanceByResourceId(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("~/resource/{id}/enhancement")]
        [SwaggerOperation(OperationId = "RemoveResourceEnhancementRecords")]
        public async Task<BaseResponse> RemoveResourceEnhancementRecords(int id)
        {
            return await _enhancementRecordService.RemoveAll(t => t.ResourceId == id);
        }

        [HttpDelete("~/media-library/{id}/enhancement")]
        [SwaggerOperation(OperationId = "RemoveMediaLibraryEnhancementRecords")]
        public async Task<BaseResponse> RemoveMediaLibraryEnhancementRecords(int id)
        {
            var resourceIds = (await _resourceService.GetAllEntities(t => t.MediaLibraryId == id))
                .Select(t => t.Id).ToArray();
            return await _enhancementRecordService.RemoveAll(t => resourceIds.Contains(t.Id));
        }

        [HttpDelete("~/category/{id}/enhancement")]
        [SwaggerOperation(OperationId = "RemoveCategoryEnhancementRecords")]
        public async Task<BaseResponse> RemoveCategoryEnhancementRecords(int id)
        {
            var resourceIds = (await _resourceService.GetAllEntities(t => t.CategoryId == id))
                .Select(t => t.Id).ToArray();
            return await _enhancementRecordService.RemoveAll(t => resourceIds.Contains(t.Id));
        }
    }
}