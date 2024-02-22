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
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services;
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
        private readonly BulkModificationService _service;
        private readonly ResourceService _resourceService;
        private readonly BulkModificationDiffService _diffService;

        public BulkModificationController(BulkModificationService service, ResourceService resourceService,
            BulkModificationDiffService diffService)
        {
            _service = service;
            _resourceService = resourceService;
            _diffService = diffService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllBulkModifications")]
        public async Task<ListResponse<BulkModificationDto>> GetAll()
        {
            return new ListResponse<BulkModificationDto>(await _service.GetAllDto());
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
            return new ListResponse<int>(await _service.PerformFiltering(id));
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "RemoveBulkModification")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpGet("{bmId:int}/diffs")]
        [SwaggerOperation(OperationId = "GetBulkModificationDiffs")]
        public async Task<ListResponse<BulkModificationDiff>> GetDiffs(int bmId)
        {
            return new ListResponse<BulkModificationDiff>(await _diffService.GetByBmId(bmId));
        }
    }
}