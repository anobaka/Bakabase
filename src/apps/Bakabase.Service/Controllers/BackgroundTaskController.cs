using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
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
            await btm.Pause(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/pause")]
        [SwaggerOperation(OperationId = "ResumeBackgroundTask")]
        public async Task<BaseResponse> Resume(string id)
        {
            await btm.Resume(id);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id}/run")]
        [SwaggerOperation(OperationId = "StopBackgroundTask")]
        public async Task<BaseResponse> Stop(string id, bool confirm = false)
        {
            var task = btm.GetTaskViewModel(id);

            // Already cancelling — short-circuit so the user doesn't see another
            // confirm prompt or a redundant cancel request.
            if (task?.Status == BTaskStatus.Cancelling)
            {
                return BaseResponseBuilder.Ok;
            }

            if (!confirm)
            {
                if (task != null && task.Status.CanBeStopped()
                    && !string.IsNullOrEmpty(task.MessageOnInterruption))
                {
                    return BaseResponseBuilder.Build((ResponseCode) 202, task.MessageOnInterruption);
                }
            }

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