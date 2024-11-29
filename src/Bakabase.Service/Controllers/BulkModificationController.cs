using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Modules.BulkModification.Models.Input;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("bulk-modification")]
    public class BulkModificationController(
        IBulkModificationService service,
        IResourceService resourceService,
        IPropertyLocalizer propertyLocalizer,
        IPropertyService propertyService,
        IBulkModificationLocalizer localizer
    )
        : Controller
    {
        [HttpGet("{id:int}")]
        [SwaggerOperation(OperationId = "GetBulkModification")]
        public async Task<SingletonResponse<BulkModificationViewModel?>> Get(int id)
        {
            var domainModel = await service.Get(id);
            var viewModel = domainModel?.ToViewModel(propertyLocalizer);
            return new SingletonResponse<BulkModificationViewModel?>(viewModel);
        }

        [HttpGet("all")]
        [SwaggerOperation(OperationId = "GetAllBulkModifications")]
        public async Task<ListResponse<BulkModificationViewModel>> GetAll()
        {
            var domainModels = await service.GetAll();
            var viewModels = domainModels.Select(x => x.ToViewModel(propertyLocalizer));
            return new ListResponse<BulkModificationViewModel>(viewModels);
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddBulkModification")]
        public async Task<BaseResponse> Add()
        {
            await service.Add(new BulkModification
                {CreatedAt = DateTime.Now, IsActive = true, Name = localizer.DefaultName()});
            return BaseResponseBuilder.Ok;
        }


        [HttpPost("{id:int}")]
        [SwaggerOperation(OperationId = "DuplicateBulkModification")]
        public async Task<BaseResponse> Duplicate(int id)
        {
            await service.Duplicate(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPatch("{id:int}")]
        [SwaggerOperation(OperationId = "PatchBulkModification")]
        public async Task<BaseResponse> Patch(int id, [FromBody] BulkModificationPatchInputModel model)
        {
            var domainModel = await model.ToDomainModel(propertyService);
            await service.Patch(id, domainModel);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "DeleteBulkModification")]
        public async Task<BaseResponse> Delete(int id)
        {
            await service.Delete(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("{id:int}/filtered-resources")]
        [SwaggerOperation(OperationId = "FilterResourcesInBulkModification")]
        public async Task<BaseResponse> Filter(int id)
        {
            await service.Filter(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPut("{id:int}/preview")]
        [SwaggerOperation(OperationId = "PreviewBulkModification")]
        public async Task<BaseResponse> Preview(int id)
        {
            await service.Preview(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("{bmId:int}/diffs")]
        [SwaggerOperation(OperationId = "SearchBulkModificationDiffs")]
        public async Task<SearchResponse<BulkModificationDiffViewModel>> SearchDiffs(int bmId, 
            BulkModificationResourceDiffsSearchInputModel model)
        {
            var result = await service.SearchDiffs(bmId, model);
            if (result.Data == null)
            {
                return new SearchResponse<BulkModificationDiffViewModel>();
            }

            var viewModels = await result.Data.ToViewModels(propertyService, propertyLocalizer);
            return model.BuildResponse(viewModels, result.TotalCount);
        }

        [HttpPost("{id:int}/apply")]
        [SwaggerOperation(OperationId = "ApplyBulkModification")]
        public async Task<BaseResponse> Apply(int id)
        {
            await service.Apply(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id:int}/apply")]
        [SwaggerOperation(OperationId = "RevertBulkModification")]
        public async Task<BaseResponse> Revert(int id)
        {
            await service.Revert(id);
            return BaseResponseBuilder.Ok;
        }
    }
}