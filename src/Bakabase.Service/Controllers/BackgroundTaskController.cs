using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/background-task")]
    public class BackgroundTaskController(BTaskManager btm) : Controller
    {
        [HttpPost("{id}/run")]
        [SwaggerOperation(OperationId = "StartBackgroundTask")]
        public async Task<BaseResponse> Start(string id)
        {
            await btm.Start(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("{id}/pause")]
        [SwaggerOperation(OperationId = "PauseBackgroundTask")]
        public async Task<BaseResponse> Pause(string id)
        {
            btm.Pause(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/pause")]
        [SwaggerOperation(OperationId = "ResumeBackgroundTask")]
        public Task<BaseResponse> Resume(string id)
        {
            btm.Resume(id);
            return Task.FromResult(BaseResponseBuilder.Ok);
        }

        [HttpDelete("{id}/run")]
        [SwaggerOperation(OperationId = "StopBackgroundTask")]
        public async Task<BaseResponse> Stop(string id)
        {
            await btm.Stop(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete]
        [SwaggerOperation(OperationId = "CleanInactiveBackgroundTasks")]
        public async Task<BaseResponse> CleanInactive()
        {
            await btm.CleanInactive();
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "CleanBackgroundTask")]
        public async Task<BaseResponse> Clean(string id)
        {
            await btm.Clean(id);
            return BaseResponseBuilder.Ok;
        }
    }
}