using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/updater")]
    public class UpdaterController : Controller
    {
        private readonly AppUpdater _appUpdater;
        private readonly IHostApplicationLifetime _lifetime;

        public UpdaterController(AppUpdater appUpdater, IHostApplicationLifetime lifetime)
        {
            _appUpdater = appUpdater;
            _lifetime = lifetime;
        }

        [HttpGet("app/new-version")]
        [SwaggerOperation(OperationId = "GetNewAppVersion")]
        public async Task<SingletonResponse<AppVersionInfo>> CheckNewAppVersion()
        {
            return new SingletonResponse<AppVersionInfo>(await _appUpdater.CheckNewVersion());
        }

        [HttpPost("app/update")]
        [SwaggerOperation(OperationId = "StartUpdatingApp")]
        public async Task<BaseResponse> StartUpdatingApp()
        {
            return await _appUpdater.StartUpdating();
        }

        [HttpDelete("app/update")]
        [SwaggerOperation(OperationId = "StopUpdatingApp")]
        public async Task<BaseResponse> StopUpdatingApp()
        {
            _appUpdater.StopUpdating();
            return BaseResponseBuilder.Ok;
        }

        [HttpPost("app/restart")]
        [SwaggerOperation(OperationId = "RestartAndUpdateApp")]
        public async Task<BaseResponse> RestartAndUpdateApp()
        {
            await _appUpdater.StartUpdater();
            _lifetime.StopApplication();
            return BaseResponseBuilder.Ok;
        }
    }
}