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
        private readonly UpdaterUpdater _updaterUpdater;
        private readonly IHostApplicationLifetime _lifetime;

        public UpdaterController(AppUpdater appUpdater, IHostApplicationLifetime lifetime,
            UpdaterUpdater updaterUpdater)
        {
            _appUpdater = appUpdater;
            _lifetime = lifetime;
            _updaterUpdater = updaterUpdater;
        }

        [HttpGet("updater/state")]
        [SwaggerOperation(OperationId = "GetUpdaterUpdaterState")]
        public async Task<SingletonResponse<UpdaterState>> GetUpdaterUpdaterState()
        {
            return new SingletonResponse<UpdaterState>(_updaterUpdater.State);
        }

        [HttpGet("updater/update")]
        [SwaggerOperation(OperationId = "StartUpdatingUpdater")]
        public async Task<BaseResponse> StartUpdatingUpdater()
        {
            return await _updaterUpdater.StartUpdating();
        }

        [HttpGet("app/new-version")]
        [SwaggerOperation(OperationId = "GetNewAppVersion")]
        public async Task<SingletonResponse<AppVersionInfo>> CheckNewAppVersion()
        {
            return new SingletonResponse<AppVersionInfo>(await _appUpdater.CheckNewVersion());
        }

        [HttpGet("app/state")]
        [SwaggerOperation(OperationId = "GetAppUpdaterState")]
        public async Task<SingletonResponse<UpdaterState>> GetAppUpdaterStatus()
        {
            return new SingletonResponse<UpdaterState>(_appUpdater.State);
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