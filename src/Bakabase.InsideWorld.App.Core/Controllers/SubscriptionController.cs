using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Controllers
{
    [Route("~/subscription")]
    public class SubscriptionController : Controller
    {
        private readonly SubscriptionService _service;
        private readonly SubscriptionRecordService _dataService;

        public SubscriptionController(SubscriptionService service, SubscriptionRecordService dataService)
        {
            _service = service;
            _dataService = dataService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllSubscriptions")]
        public async Task<ListResponse<SubscriptionDto>> GetAll()
        {
            return new(await _service.GetAll());
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddSubscription")]
        public async Task<BaseResponse> Add([FromBody] SubscriptionAddOrUpdateRequestModel model)
        {
            return await _service.Add(model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveSubscription")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "UpdateSubscription")]
        public async Task<BaseResponse> Update(int id, [FromBody] SubscriptionAddOrUpdateRequestModel model)
        {
            return await _service.Update(id, model);
        }

        [HttpPost("synchronization")]
        [SwaggerOperation(OperationId = "StartSyncingSubscriptions")]
        public async Task<BaseResponse> Sync()
        {
            return await _service.StartSync(null);
        }

        [HttpDelete("synchronization")]
        [SwaggerOperation(OperationId = "StopSyncingSubscriptions")]
        public async Task<BaseResponse> StopSyncing()
        {
            await _service.StopSyncing();
            return BaseResponseBuilder.Ok;
        }

        [HttpGet("synchronization")]
        [SwaggerOperation(OperationId = "GetSubscriptionSyncStatus")]
        public async Task<SingletonResponse<SubscriptionSyncStatus>> GetSyncStatus()
        {
            return new SingletonResponse<SubscriptionSyncStatus>(await _service.GetSyncStatus());
        }

        [HttpGet("data")]
        [SwaggerOperation(OperationId = "GetAllSubscriptionData")]
        public async Task<ListResponse<SubscriptionRecord>> GetAllData()
        {
            return new ListResponse<SubscriptionRecord>(await _dataService.GetAll());
        }

        [HttpPut("{id}/records/read")]
        [SwaggerOperation(OperationId = "ReadAllRecordsBySubscriptionId")]
        public async Task<BaseResponse> ReadAllRecordsBySubscriptionId(int id)
        {
            return await _dataService.UpdateAll(a => a.SubscriptionId == id, a => a.Read = true);
        }
    }
}