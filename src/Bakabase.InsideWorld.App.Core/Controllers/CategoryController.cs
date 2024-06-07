using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Input;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Input;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/category")]
    public class CategoryController(
        ICategoryService service,
        ICategoryEnhancerOptionsService categoryEnhancerOptionsService)
        : Controller
    {
        [HttpGet("{id:int}")]
        [SwaggerOperation(OperationId = "GetCategory")]
        public async Task<SingletonResponse<Category?>> Get(int id,
            [FromQuery] CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            return new SingletonResponse<Category?>(await service.Get(id, additionalItems));
        }


        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllCategories")]
        public async Task<ListResponse<Category>> GetAll(
            [FromQuery] CategoryAdditionalItem additionalItems = CategoryAdditionalItem.None)
        {
            return new ListResponse<Category>(await service.GetAll(null, additionalItems));
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

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "PatchCategory")]
        public async Task<BaseResponse> Patch(int id, [FromBody] CategoryPatchInputModel model)
        {
            return await service.Patch(id, model);
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

        [HttpPatch("{id:int}/enhancer/{enhancerId:int}/options")]
        [SwaggerOperation(OperationId = "PatchCategoryEnhancerOptions")]
        public async Task<BaseResponse> PatchEnhancerOptions(int id, int enhancerId,
            [FromBody] CategoryEnhancerOptionsPatchInputModel model)
        {
            return await categoryEnhancerOptionsService.Patch(id, enhancerId, model);
        }
    }
}