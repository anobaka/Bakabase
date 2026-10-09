using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Bakabase.Service.Components;
using Bakabase.Service.Components.ServerData;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/updater")]
    public class UpdaterController : Controller
    {
        private readonly AppUpdater _appUpdater;
        private readonly IGuiAdapter _guiAdapter;
        private readonly IHostApplicationLifetime _lifetime;

        public UpdaterController(AppUpdater appUpdater, IGuiAdapter guiAdapter, IHostApplicationLifetime lifetime)
        {
            _appUpdater = appUpdater;
            _guiAdapter = guiAdapter;
            _lifetime = lifetime;
        }

        [HttpGet("app/new-version")]
        [SwaggerOperation(OperationId = "GetNewAppVersion")]
        public async Task<SingletonResponse<AppVersionInfo>> CheckNewAppVersion()
        {
            if (_guiAdapter is NullGuiAdapter)
                return new SingletonResponse<AppVersionInfo>(new AppVersionInfo
                {
                    RunningVersion = ServerAppDataImport.RunningVersion.ToString(),
                    UpdateCheckUnavailable = true
                });
            return new SingletonResponse<AppVersionInfo>(await _appUpdater.CheckNewVersion());
        }

        [HttpPost("app/update")]
        [SwaggerOperation(OperationId = "StartUpdatingApp")]
        public async Task<BaseResponse> StartUpdatingApp()
        {
            if (_guiAdapter is NullGuiAdapter) return ServerUpdateResponse();
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
        public BaseResponse RestartAndUpdateApp()
        {
            if (_guiAdapter is NullGuiAdapter) return ServerUpdateResponse();
            // Validate the downloaded package before acknowledging the request. The response
            // must finish before stopping the web host; the shell then keeps a native progress
            // window visible while it releases tasks, database connections and the host.
            var launchUpdater = _appUpdater.PrepareUpdateRestart(SetupChildConnection.Current?.ParentProcessId);
            if (_guiAdapter is IUpdateRestartCoordinator coordinator)
            {
                if (!coordinator.TryReserveUpdateRestart(launchUpdater))
                {
                    return BaseResponseBuilder.BuildBadRequest("Bakabase is already closing or updating.");
                }

                Response.OnCompleted(() =>
                {
                    coordinator.BeginReservedUpdateRestart();
                    return Task.CompletedTask;
                });
                // OnCompleted is not guaranteed if the browser disconnects during the
                // response. The user already requested the update, so start the same
                // reserved path on abort too; the coordinator ignores a duplicate start.
                HttpContext.RequestAborted.Register(coordinator.BeginReservedUpdateRestart);
            }
            else
            {
                if (_lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    return BaseResponseBuilder.BuildBadRequest("Bakabase is already closing.");
                }

                // Headless host: its services stop before ApplicationStopped starts Velopack.
                _lifetime.ApplicationStopped.Register(launchUpdater);
                Response.OnCompleted(() =>
                {
                    _lifetime.StopApplication();
                    return Task.CompletedTask;
                });
                HttpContext.RequestAborted.Register(_lifetime.StopApplication);
            }

            return BaseResponseBuilder.Ok;
        }

        private static BaseResponse ServerUpdateResponse() => BaseResponseBuilder.BuildBadRequest(
            "Update the standalone server by replacing its Docker image or server release, then restart with the same appdata directory.");
    }
}
