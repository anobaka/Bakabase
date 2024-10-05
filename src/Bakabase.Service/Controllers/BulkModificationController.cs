using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("bulk-modification")]
    public class BulkModificationController : Controller
    {
        private readonly BulkModificationService _service;
        private readonly IResourceService _resourceService;
        private readonly BulkModificationDiffService _diffService;
        private readonly BulkModificationTempDataService _tempDataService;

        public BulkModificationController(BulkModificationService service, IResourceService resourceService,
            BulkModificationDiffService diffService, BulkModificationTempDataService tempDataService)
        {
            _service = service;
            _resourceService = resourceService;
            _diffService = diffService;
            _tempDataService = tempDataService;
        }

        [HttpGet("{id:int}")]
        [SwaggerOperation(OperationId = "GetBulkModificationById")]
        public async Task<SingletonResponse<BulkModificationDto>> Get(int id)
        {
            return new SingletonResponse<BulkModificationDto>(await _service.GetDto(id));
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllBulkModifications")]
        public async Task<ListResponse<BulkModificationDto>> GetAll()
        {
            return new ListResponse<BulkModificationDto>(
                (await _service.GetAllDto()).OrderByDescending(x => x.CreatedAt));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "CreateBulkModification")]
        public async Task<SingletonResponse<BulkModificationDto>> Add([FromBody] BulkModificationPutRequestModel model)
        {
            var data = await _service.Add(new BulkModification
            {
                Name = model.Name,
                Filter = JsonConvert.SerializeObject(model.Filter),
                Processes = JsonConvert.SerializeObject(model.Processes),
                Variables = JsonConvert.SerializeObject(model.Variables),
            });
            return new SingletonResponse<BulkModificationDto>(data.Data.ToDto(null));
        }

        [HttpPost("{id:int}/duplication")]
        [SwaggerOperation(OperationId = "DuplicateBulkModification")]
        public async Task<SingletonResponse<BulkModificationDto>> Duplicate(int id)
        {
            return new SingletonResponse<BulkModificationDto>(await _service.Duplicate(id));
        }

        [HttpPut("{id:int}/close")]
        [SwaggerOperation(OperationId = "CloseBulkModification")]
        public async Task<BaseResponse> Close(int id)
        {
            return await _service.Close(id);
        }

        [HttpPut("{id:int}")]
        [SwaggerOperation(OperationId = "PutBulkModification")]
        public async Task<BaseResponse> Put(int id, [FromBody] BulkModificationPutRequestModel model)
        {
            return await _service.UpdateByKey(id, d =>
            {
                d.Name = model.Name;
                d.Filter = JsonConvert.SerializeObject(model.Filter);
                d.Processes = JsonConvert.SerializeObject(model.Processes);
                d.Variables = JsonConvert.SerializeObject(model.Variables);
            });
        }

        [HttpPut("{id:int}/filter")]
        [SwaggerOperation(OperationId = "PerformBulkModificationFiltering")]
        public async Task<ListResponse<int>> PerformFiltering(int id)
        {
            return await _service.PerformFiltering(id);
        }

        [HttpGet("{id:int}/filtered-resources")]
        [SwaggerOperation(OperationId = "GetBulkModificationFilteredResources")]
        public async Task<ListResponse<Resource>> GetFilteredResources(int id)
        {
            var ids = (await _tempDataService.GetByKey(id))?.GetResourceIds().ToArray();
            if (ids == null)
            {
                return new ListResponse<Resource>(new List<Resource>());
            }

            var resources = await _resourceService.GetByKeys(ids);
            return new ListResponse<Resource>(resources);
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "DeleteBulkModification")]
        public async Task<BaseResponse> Delete(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpGet("{bmId:int}/diffs")]
        [SwaggerOperation(OperationId = "GetBulkModificationResourceDiffs")]
        public async Task<ListResponse<BulkModificationResourceDiffs>> GetDiffs(int bmId)
        {
            var diffs = await _diffService.GetByBmId(bmId);
            return new ListResponse<BulkModificationResourceDiffs>(diffs.GroupBy(a => a.ResourceId).Select(x =>
                new BulkModificationResourceDiffs
                {
                    Id = x.Key,
                    Path = x.First().ResourcePath,
                    Diffs = x.Select(b => new BulkModificationResourceDiffs.Diff
                    {
                        Property = b.Property,
                        PropertyKey = b.PropertyKey,
                        CurrentValue = b.CurrentValue,
                        NewValue = b.NewValue,
                        Operation = b.Operation,
                        Type = b.Type
                    }).ToList()
                }));
        }

        [HttpPost("{id:int}/diffs")]
        [SwaggerOperation(OperationId = "CalculateBulkModificationResourceDiffs")]
        public async Task<BaseResponse> Preview(int id)
        {
            var data = await _service.Preview(id, HttpContext.RequestAborted);
            return new BaseResponse(data.Code, data.Message);
        }

        [HttpPost("{id:int}/apply")]
        [SwaggerOperation(OperationId = "ApplyBulkModification")]
        public async Task<BaseResponse> Apply(int id)
        {
            return await _service.Apply(id);
        }

        [HttpPost("{id:int}/revert")]
        [SwaggerOperation(OperationId = "RevertBulkModification")]
        public async Task<BaseResponse> Revert(int id)
        {
            return await _service.Revert(id);
        }
    }
}