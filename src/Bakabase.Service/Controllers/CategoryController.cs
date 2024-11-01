using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Input;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/category")]
    public class CategoryController(
        ICategoryService service,
        ICategoryEnhancerOptionsService categoryEnhancerOptionsService,
        IMediaLibraryService mediaLibraryService
    )
        : Controller
    {
        [HttpGet("{id:int}")]
        [SwaggerOperation(OperationId = "GetCategory")]
        public async Task<SingletonResponse<CategoryViewModel?>> Get(int id,
            [FromQuery] CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            return new SingletonResponse<CategoryViewModel?>((await service.Get(id, additionalItems))?.ToViewModel());
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllCategories")]
        public async Task<ListResponse<CategoryViewModel>> GetAll(
            [FromQuery] CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            return new ListResponse<CategoryViewModel>(
                (await service.GetAll(null, additionalItems))?.Select(x => x.ToViewModel()));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddCategory")]
        public async Task<BaseResponse> Add([FromBody] CategoryAddInputModel model)
        {
            return await service.Add(model);
        }

        [HttpPost("{id:int}/duplication")]
        [SwaggerOperation(OperationId = "DuplicateCategory")]
        public async Task<BaseResponse> Duplicate(int id, [FromBody] CategoryDuplicateInputModel model)
        {
            return await service.Duplicate(id, model);
        }

        [HttpPatch("{id}")]
        [SwaggerOperation(OperationId = "PatchCategory")]
        public async Task<BaseResponse> Patch(int id, [FromBody] CategoryPatchInputModel model)
        {
            return await service.Patch(id, model);
        }

        [HttpPut("{id}/resource-display-name-template")]
        [SwaggerOperation(OperationId = "PutCategoryResourceDisplayNameTemplate")]
        public async Task<BaseResponse> PutResourceDisplayNameTemplate(int id, [FromBody] string template)
        {
            return await service.PutResourceDisplayNameTemplate(id, template);
        }

        [HttpPut("{id}/component")]
        [SwaggerOperation(OperationId = "ConfigureCategoryComponents")]
        public async Task<BaseResponse> ConfigureComponents(int id,
            [FromBody] CategoryComponentConfigureInputModel model)
        {
            return await service.ConfigureComponents(id, model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "DeleteCategory")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await service.Delete(id);
        }

        [HttpPut("orders")]
        [SwaggerOperation(OperationId = "SortCategories")]
        public async Task<BaseResponse> Sort([FromBody] IdBasedSortRequestModel model)
        {
            return await service.Sort(model.Ids);
        }

        // [HttpPost("setup-wizard")]
        // [SwaggerOperation(OperationId = "SaveDataFromSetupWizard")]
        // public async Task<BaseResponse> SaveDataFromSetupWizard([FromBody] CategorySetupWizardInputModel model)
        // {
        //     return await service.SaveDataFromSetupWizard(model);
        // }

        [HttpPut("{id:int}/custom-properties")]
        [SwaggerOperation(OperationId = "BindCustomPropertiesToCategory")]
        public async Task<BaseResponse> BindCustomProperties(int id,
            [FromBody] CategoryCustomPropertyBindInputModel model)
        {
            return await service.BindCustomProperties(id, model);
        }

        [HttpPost("{categoryId:int}/custom-property/{customPropertyId:int}")]
        [SwaggerOperation(OperationId = "BindCustomPropertyToCategory")]
        public async Task<BaseResponse> BindCustomProperty(int categoryId, int customPropertyId)
        {
            return await service.BindCustomProperty(categoryId, customPropertyId);
        }

        [HttpGet("{id:int}/resource/resource-display-name-template/preview")]
        [SwaggerOperation(OperationId = "PreviewCategoryDisplayNameTemplate")]
        public async Task<ListResponse<CategoryResourceDisplayNameViewModel>> PreviewResourceDisplayNameTemplate(int id,
            string template, int maxCount = 100)
        {
            var result = await service.PreviewResourceDisplayNameTemplate(id, template, maxCount);
            return new ListResponse<CategoryResourceDisplayNameViewModel>(result);
        }

        [HttpGet("{id:int}/enhancer/{enhancerId:int}/options")]
        [SwaggerOperation(OperationId = "GetCategoryEnhancerOptions")]
        public async Task<SingletonResponse<CategoryEnhancerOptions?>> GetEnhancerOptions(int id, int enhancerId)
        {
            return new SingletonResponse<CategoryEnhancerOptions?>(
                await categoryEnhancerOptionsService.GetByCategoryAndEnhancer(id, enhancerId));
        }

        [HttpPatch("{id:int}/enhancer/{enhancerId:int}/options")]
        [SwaggerOperation(OperationId = "PatchCategoryEnhancerOptions")]
        public async Task<BaseResponse> PatchEnhancerOptions(int id, int enhancerId,
            [FromBody] CategoryEnhancerOptionsPatchInputModel model)
        {
            return await categoryEnhancerOptionsService.Patch(id, enhancerId, model);
        }

        [HttpDelete("{id:int}/enhancer/{enhancerId:int}/options/target")]
        [SwaggerOperation(OperationId = "DeleteCategoryEnhancerTargetOptions")]
        public async Task<BaseResponse> DeleteEnhancerTargetOptions(int id, int enhancerId, int target,
            string? dynamicTarget)
        {
            return await categoryEnhancerOptionsService.DeleteTarget(id, enhancerId, target, dynamicTarget);
        }

        [HttpPatch("{id:int}/enhancer/{enhancerId:int}/options/target")]
        [SwaggerOperation(OperationId = "PatchCategoryEnhancerTargetOptions")]
        public async Task<BaseResponse> PatchEnhancerTargetOptions(int id, int enhancerId, [Required] int target,
            string? dynamicTarget,
            [FromBody] CategoryEnhancerTargetOptionsPatchInputModel patches)
        {
            return await categoryEnhancerOptionsService.PatchTarget(id, enhancerId, target, dynamicTarget, patches);
        }

        [HttpDelete("{id:int}/enhancer/{enhancerId:int}/options/target/property")]
        [SwaggerOperation(OperationId = "UnbindCategoryEnhancerTargetProperty")]
        public async Task<BaseResponse> UnbindEnhancerTargetProperty(int id, int enhancerId, [Required] int target,
            string? dynamicTarget)
        {
            return await categoryEnhancerOptionsService.UnbindTargetProperty(id, enhancerId, target, dynamicTarget);
        }

        [HttpPut("{id:int}/synchronization")]
        [SwaggerOperation(OperationId = "StartSyncingCategoryResources")]
        public async Task<BaseResponse> StartSyncing(int id)
        {
            mediaLibraryService.StartSyncing([id], null);
            return BaseResponseBuilder.Ok;
        }
    }
}