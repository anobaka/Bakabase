using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/background-task")]
    public class BackgroundTaskController : Controller
    {
        private readonly BackgroundTaskManager _btm;

        public BackgroundTaskController(BackgroundTaskManager btm)
        {
            _btm = btm;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllBackgroundTasks")]
        public async Task<ListResponse<BackgroundTaskDto>> GetAll()
        {
            return new(_btm.Tasks);
        }

        [HttpGet("by-name")]
        [SwaggerOperation(OperationId = "GetBackgroundTaskByName")]
        public async Task<SingletonResponse<BackgroundTaskDto>> GetByName(string name)
        {
            return new SingletonResponse<BackgroundTaskDto>(_btm.GetByName(name).FirstOrDefault());
        }

        [HttpDelete]
        [SwaggerOperation(OperationId = "ClearInactiveBackgroundTasks")]
        public BaseResponse ClearInactive()
        {
            _btm.ClearInactive();
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/stop")]
        [SwaggerOperation(OperationId = "StopBackgroundTask")]
        public BaseResponse Stop(string id)
        {
            _btm.Stop(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveBackgroundTask")]
        public async Task<BaseResponse> Delete(string id)
        {
            _btm.Remove(id);
            return BaseResponseBuilder.Ok;
        }
    }
}